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

Each candidate gets its own `claude -p` invocation, started from a guaranteed-clean checkout
of its valid branch: `main` for an issue (force-reset to origin/main, untracked files
removed), the PR's head branch for a PR. The script prepares that checkout itself, so the CLI
run never starts from a stale or dirty tree - and a batch of candidates needing different
branches can't contaminate each other.

Single-instance lock: acquires an exclusive OS lock on Logs/handle_issues_claude.lock before
doing anything else. If a previous run is still in flight when the next cron tick fires, this
run exits immediately instead of racing it - the lock releases automatically even if a prior
run crashed, since it's tied to the OS file descriptor, not manually cleared state.

Stale-run reclaim: because the lock guarantees no other run is active, any item still labeled
`claude-in-progress` at startup is leftover from a crashed/interrupted run. It's re-queued (the
label removed, a `<!-- claude-automation:reclaim -->` comment posted as the attempt counter) up
to twice; a third consecutive crash parks it with `claude-needs-attention` instead of retrying
forever. A real owner comment resets the counter.

Session/usage limits are NOT crashes: when the run's error output reports the subscription's
session/usage limit, this script records the window's reset time (from the CLI's reported
epoch when present, else now + --limit-backoff-minutes) as an aware-UTC timestamp in
Logs/handle_issues_claude.limit.json, silently removes the `claude-in-progress` labels the
interrupted run left behind (no reclaim comment, no crash-retry consumed, no error noise on
the item), and exits 0. Every later run compares that timestamp against the current time -
both aware UTC, so the machine's local timezone never skews it - and skips entirely (before
any GitHub call) until the window has passed.

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
import re
import shutil
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from common.issue_handler import (  # noqa: E402
    acquire_lock, candidate_branch, checkout_clean, find_candidates, limit_active,
    reclaim_stale_in_progress, release_in_progress_silently, save_limit_retry_at,
    setup_logging,
)

MODEL = "claude-sonnet-5"
EFFORT = "high"
LABEL = "claude"
MARKER = "<!-- claude-automation -->"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_claude.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_claude.lock"
DEFAULT_LIMIT_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_claude.limit.json"
DEFAULT_LIMIT_BACKOFF_MINUTES = 60

logger = logging.getLogger("handle_issues_claude")

# Subscription session/usage limits surface either as a plain-text output line (e.g.
# "Claude AI usage limit reached|1735689600" - epoch seconds of the window reset) or inside
# the final result event's error text. Detection deliberately looks ONLY at error output
# (non-JSON lines and the error result), never at assistant/tool text - an issue whose prompt
# merely talks about usage limits must not trigger a false positive.
LIMIT_TEXT_RE = re.compile(r"(usage|session) limit reached", re.IGNORECASE)
LIMIT_EPOCH_RE = re.compile(r"limit reached\|(\d{10,})")


def detect_session_limit(error_texts):
    """Returns (limit_hit, retry_at). retry_at is an aware-UTC datetime when the CLI reported
    the window's reset epoch, else None (caller falls back to a fixed backoff)."""
    joined = "\n".join(error_texts)
    match = LIMIT_EPOCH_RE.search(joined)
    if match:
        return True, datetime.fromtimestamp(int(match.group(1)), tz=timezone.utc)
    if LIMIT_TEXT_RE.search(joined):
        return True, None
    return False, None


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


def build_prompt(candidate):
    if candidate["kind"] == "pr":
        kind = "PR"
        header = f"[PR #{candidate['number']}] {candidate['url']} (head branch: {candidate['headRefName']})"
        checkout = f"the PR's head branch '{candidate['headRefName']}'"
    else:
        kind = "ISSUE"
        header = f"[ISSUE #{candidate['number']}] {candidate['url']}"
        checkout = "'main'"
    return (
        "/handle-issue\n\n"
        f"The following GitHub {kind.lower()} is labeled 'claude', authored by the repo owner, "
        "and carries no automation status label. Process it per the command's rules. The "
        f"working tree is already a guaranteed-clean, up-to-date checkout of {checkout}. The "
        "description shown here may be stale - re-read the item's live description and comment "
        "thread yourself. Do not process any other issues or PRs, this item is this run's only "
        "candidate:\n\n"
        f"{header}\n{candidate['title']}\n\n{candidate['body'] or '(empty body)'}"
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--max-turns", type=int, default=40)
    parser.add_argument("--log-file", type=Path, default=DEFAULT_LOG_FILE)
    parser.add_argument("--log-max-bytes", type=int, default=DEFAULT_LOG_MAX_BYTES)
    parser.add_argument("--log-backup-count", type=int, default=DEFAULT_LOG_BACKUP_COUNT)
    parser.add_argument("--lock-file", type=Path, default=DEFAULT_LOCK_FILE)
    parser.add_argument("--limit-file", type=Path, default=DEFAULT_LIMIT_FILE,
                         help="Stores the aware-UTC timestamp until which runs are skipped "
                              "after a session/usage-limit hit.")
    parser.add_argument("--limit-backoff-minutes", type=int, default=DEFAULT_LIMIT_BACKOFF_MINUTES,
                         help="How long to pause after a limit hit when the CLI's error text "
                              "doesn't include the window's reset time.")
    args = parser.parse_args()

    setup_logging(logger, args.log_file, args.log_max_bytes, args.log_backup_count)

    lock = acquire_lock(logger, args.lock_file)
    if lock is None:
        logger.info("Another instance is already running - exiting.")
        return

    if limit_active(logger, args.limit_file):
        return

    reclaim_stale_in_progress(logger, LABEL, MARKER)
    candidates = find_candidates(LABEL)

    if not candidates:
        logger.info(f"No '{LABEL}'-labeled items without a status label - nothing to do.")
        return

    logger.info(f"Found {len(candidates)} candidate(s).")
    exit_code = 0
    for candidate in candidates:
        branch = candidate_branch(candidate)
        checkout_clean(logger, branch)
        logger.info(f"Processing {candidate['kind']} #{candidate['number']} from clean "
                     f"'{branch}' - invoking claude -p.")
        returncode, error_texts = run_claude(build_prompt(candidate), args.max_turns)
        if returncode != 0:
            exit_code = returncode
        limit_hit, retry_at = detect_session_limit(error_texts)
        if limit_hit:
            if retry_at is None:
                retry_at = datetime.now(timezone.utc) + timedelta(minutes=args.limit_backoff_minutes)
            save_limit_retry_at(args.limit_file, retry_at)
            release_in_progress_silently(logger, LABEL)
            logger.warning(f"Session/usage limit hit - pausing runs until {retry_at.isoformat()}. "
                            "This is a planned pause, not a failure (exit 0); interrupted and "
                            "remaining items stay in the candidate pool without consuming a "
                            "crash retry.")
            sys.exit(0)
    sys.exit(exit_code)


def run_claude(prompt, max_turns):
    """Runs one claude -p invocation to completion, streaming its events into the log.
    Returns (returncode, error_texts) - error_texts is the CLI's error output only (non-JSON
    lines plus the final result event's text when it reports an error), the input for
    session-limit detection."""
    process = subprocess.Popen(
        [
            find_claude_executable(), "-p", prompt,
            "--model", MODEL,
            "--effort", EFFORT,
            "--output-format", "stream-json",
            "--verbose",
            "--dangerously-skip-permissions",
            "--max-turns", str(max_turns),
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        bufsize=1,
    )
    error_texts = []
    result_event = None
    for line in process.stdout:
        line = line.rstrip()
        if not line:
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            error_texts.append(line)  # non-JSON output = CLI error text, e.g. a limit message
            logger.info(f"[claude -p] {line}")
            continue
        if event.get("type") == "result":
            result_event = event
        summary = summarize_stream_event(event)
        if summary:
            logger.info(f"[claude -p] {summary}")
    process.wait()
    logger.info(f"claude -p exited with code {process.returncode}.")

    if result_event and (result_event.get("is_error")
                         or str(result_event.get("subtype", "")).startswith("error")):
        error_texts.append(str(result_event.get("result", "")))
    return process.returncode, error_texts


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_issues.py failed")
        sys.exit(1)
