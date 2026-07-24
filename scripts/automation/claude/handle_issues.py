#!/usr/bin/env python3
"""Handle Labeled Issues (Claude) - polls GitHub and executes the owner's prompts from
`claude`-labeled issues and PRs.

Meant to run on a schedule (cron / Task Scheduler) in the user's own environment, not in a
CI runner or Claude Code Remote session - it uses whatever `gh` auth and `claude` login
(subscription-based, not an API key) already exist on the machine it runs on.

Cheap discovery, expensive work gated behind it: this script does the "is there anything to
do at all" check itself, via plain `gh` calls (no LLM usage). `claude -p` is only invoked -
and only then does it spend subscription usage - when discovery actually finds something.

The label set is the whole state machine (no local state file, no timestamps):

  claude                  the owner opted an issue/PR in; its description + owner comments
                          are the prompt to execute
  claude-in-progress      a run is actively working it (skipped by discovery)
  claude-needs-attention  waiting on the owner (skipped by discovery)
  claude-complete         prompt fully done (skipped by discovery)

A candidate is any open, owner-authored, `claude`-labeled issue/PR carrying none of the three
status labels. The owner resumes a needs-attention/complete item by replying and removing that
label. See .claude/commands/handle-issue.md for the per-item lifecycle the CLI run follows and
the `github-issue-automation` skill for the full design writeup.

Single-instance lock: acquires an exclusive OS lock on Logs/handle_issues_claude.lock before
doing anything else. If a previous run is still in flight when the next cron tick fires, this
run exits immediately instead of racing it - the lock releases automatically even if a prior
run crashed, since it's tied to the OS file descriptor, not manually cleared state.

Stale-run reclaim: because the lock guarantees no other run is active, any item still labeled
`claude-in-progress` at startup is leftover from a crashed/interrupted run. It's re-queued (the
label removed, a `<!-- claude-automation:reclaim -->` comment posted as the attempt counter) up
to twice; a third consecutive crash parks it with `claude-needs-attention` instead of retrying
forever. A real owner comment resets the counter.

Requires `gh` authenticated as the repo owner (`gh auth login`), the labels above already
created in the repo (see handle_issues.sh for the `gh label create` commands), and `claude`
logged into a subscription (`claude` with no ANTHROPIC_API_KEY set). Runs explicitly on
Sonnet 5 at high reasoning effort - unattended, scheduled work with no one watching to catch
a model/effort default drifting under it.

Logs to Logs/handle_issues_claude.log (gitignored, same as Unity's own Logs/ folder) with
size-based auto-rotation - no unbounded append, no manual cleanup needed. Override with
--log-file/--log-max-bytes/--log-backup-count if you want it elsewhere.

claude -p runs with --output-format stream-json --verbose, streamed and parsed line-by-line
(each line is a self-contained JSON event) so the actual tool calls, assistant text, and final
result land in the same log, not just "invoked, exited with code N". Noisy bookkeeping events
(rate-limit pings, the full skill-list dump on startup, etc.) are filtered out; assistant
turns and the final result are kept.

Usage (from project root):
  python scripts/automation/claude/handle_issues.py
  python scripts/automation/claude/handle_issues.py --max-turns 60

Shared discovery/locking/reclaim logic lives in scripts/automation/common/issue_handler.py -
this file only supplies what's specific to driving Claude Code: CLI invocation, stream-json
parsing, and prompt text.
"""

import argparse
import json
import logging
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from common.issue_handler import (  # noqa: E402
    acquire_lock, find_candidates, reclaim_stale_in_progress, reset_to_main, setup_logging,
)

MODEL = "claude-sonnet-5"
EFFORT = "high"
LABEL = "claude"
MARKER = "<!-- claude-automation -->"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_claude.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_claude.lock"

logger = logging.getLogger("handle_issues_claude")


def find_claude_executable():
    return shutil.which("claude") or "claude"


def summarize_stream_event(obj):
    """Turn one --output-format stream-json line into a readable log line, or None to skip it."""
    etype = obj.get("type")

    if etype in ("assistant", "user"):
        message = obj.get("message", {})
        role = message.get("role", etype)
        parts = []
        for block in message.get("content") or []:
            btype = block.get("type")
            if btype == "text" and block.get("text", "").strip():
                parts.append(block["text"].strip())
            elif btype == "tool_use":
                parts.append(f"[tool_use {block.get('name')} input={json.dumps(block.get('input'))[:300]}]")
            elif btype == "tool_result":
                parts.append(f"[tool_result {str(block.get('content'))[:300]}]")
        return f"{role}: " + " | ".join(parts) if parts else None

    if etype == "result":
        return (f"result: subtype={obj.get('subtype')} success={not obj.get('is_error')} "
                f"duration_ms={obj.get('duration_ms')} turns={obj.get('num_turns')} "
                f"cost_usd={obj.get('total_cost_usd')}")

    return None  # skip bookkeeping noise: active_goal, rate_limit_event, system/*, etc.


def build_prompt(candidates):
    sections = []
    for candidate in candidates:
        kind = "PR" if candidate["kind"] == "pr" else "ISSUE"
        header = f"[{kind} #{candidate['number']}] {candidate['url']}"
        if candidate["kind"] == "pr":
            header += f" (head branch: {candidate['headRefName']})"
        sections.append(f"{header}\n{candidate['title']}\n\n{candidate['body'] or '(empty body)'}")
    joined = "\n\n---\n\n".join(sections)
    return (
        "/handle-issue\n\n"
        "The following GitHub items are labeled 'claude', authored by the repo owner, and "
        "carry no automation status label. Process each one per the command's rules. The "
        "descriptions shown here may be stale - re-read each item's live description and "
        "comment thread yourself. Do not re-scan the repo for other candidates, this list is "
        "already the full set:\n\n"
        f"{joined}"
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--max-turns", type=int, default=40)
    parser.add_argument("--log-file", type=Path, default=DEFAULT_LOG_FILE)
    parser.add_argument("--log-max-bytes", type=int, default=DEFAULT_LOG_MAX_BYTES)
    parser.add_argument("--log-backup-count", type=int, default=DEFAULT_LOG_BACKUP_COUNT)
    parser.add_argument("--lock-file", type=Path, default=DEFAULT_LOCK_FILE)
    args = parser.parse_args()

    setup_logging(logger, args.log_file, args.log_max_bytes, args.log_backup_count)

    lock = acquire_lock(logger, args.lock_file)
    if lock is None:
        logger.info("Another instance is already running - exiting.")
        return

    reclaim_stale_in_progress(logger, LABEL, MARKER)
    candidates = find_candidates(LABEL)

    if not candidates:
        logger.info(f"No '{LABEL}'-labeled items without a status label - nothing to do.")
        return

    reset_to_main(logger)

    prompt = build_prompt(candidates)
    logger.info(f"Found {len(candidates)} candidate(s) - invoking claude -p.")

    claude_exe = find_claude_executable()
    process = subprocess.Popen(
        [
            claude_exe, "-p", prompt,
            "--model", MODEL,
            "--effort", EFFORT,
            "--output-format", "stream-json",
            "--verbose",
            "--dangerously-skip-permissions",
            "--max-turns", str(args.max_turns),
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )
    for line in process.stdout:
        line = line.rstrip()
        if not line:
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            logger.info(f"[claude -p] {line}")
            continue
        summary = summarize_stream_event(event)
        if summary:
            logger.info(f"[claude -p] {summary}")
    process.wait()
    logger.info(f"claude -p exited with code {process.returncode}.")
    sys.exit(process.returncode)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_issues.py failed")
        sys.exit(1)
