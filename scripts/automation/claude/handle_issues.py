#!/usr/bin/env python3
"""Handle Feature Issues - polls GitHub for owner issues and drives them through spec/plan/merge.

Meant to run on a schedule (cron / Task Scheduler) in the user's own environment, not in a
CI runner or Claude Code Remote session - it uses whatever `gh` auth and `claude` login
(subscription-based, not an API key) already exist on the machine it runs on.

Cheap discovery, expensive work gated behind it: this script does the "is there anything to
do at all" check itself, via plain `gh` calls (no LLM usage). `claude -p` is only invoked -
and only then does it spend subscription usage - when discovery actually finds something.
Conversation happens entirely on the ISSUE (not the PR) across the whole spec -> plan ->
merge lifecycle, driven by the owner's comments and reactions - see
.claude/commands/handle-feature-issue.md for the state machine and
.claude/rules/github_issue_automation.md for the full design writeup.

A candidate issue (open, labeled `claude`, authored by the owner) is picked up if either:
  - its `updatedAt` falls inside the lookback window (covers new issues and new comments), or
  - any reaction from the owner on one of this automation's own comments (identified by the
    `<!-- claude-automation -->` marker) has a `created_at` inside the window - reactions do
    NOT bump `updatedAt` on GitHub's side, so this needs its own separate check per open
    candidate, not just a timestamp filter on the issue list itself.

Single-instance lock: acquires an exclusive OS lock on Logs/handle_feature_issues.lock before
doing anything else. If a previous run is still in flight when the next cron tick fires, this
run exits immediately instead of racing it - the lock releases automatically even if a prior
run crashed, since it's tied to the OS file descriptor, not manually cleared state.

Skipping a locked-out run must never cost a window of activity: the lookback cutoff is not
just "now minus --since-hours/--since-minutes", it's also clamped to the timestamp of the
last run that actually completed discovery (Logs/handle_feature_issues.state.json). A run
that gets skipped because the lock is held simply never advances that timestamp, so the next
run that does acquire the lock looks back at least as far as the last successful check -
covering whatever activity happened during the skipped window - instead of the fixed rolling
window silently missing anything older than --since-minutes/--since-hours by the time it
finally runs.

Requires `gh` authenticated as the repo owner (`gh auth login`), the `claude` label already
created in the repo (`gh label create claude`), and `claude` logged into a subscription
(`claude` with no ANTHROPIC_API_KEY set - see .claude/rules/github_issue_automation.md for
why this matters). Runs explicitly on Sonnet 5 at high reasoning effort - unattended,
scheduled work with no one watching to catch a model/effort default drifting under it.

Logs to Logs/handle_feature_issues.log (gitignored, same as Unity's own Logs/ folder) with
size-based auto-rotation - no unbounded append, no manual cleanup needed. Override with
--log-file/--log-max-bytes/--log-backup-count if you want it elsewhere.

claude -p runs with --output-format stream-json --verbose, streamed and parsed line-by-line
(each line is a self-contained JSON event - confirmed by direct testing against the CLI, not
just docs) so the actual tool calls, assistant text, and final result land in the same log,
not just "invoked, exited with code N". Noisy bookkeeping events (rate-limit pings, the full
skill-list dump on startup, etc.) are filtered out; assistant turns and the final result are
kept.

Usage (from project root):
  python scripts/automation/claude/handle_issues.py
  python scripts/automation/claude/handle_issues.py --since-hours 2 --max-turns 60
  python scripts/automation/claude/handle_issues.py --since-minutes 15

Shared discovery/locking/state logic lives in scripts/automation/common/issue_handler.py - this
file only supplies what's specific to driving Claude Code: CLI invocation, stream-json parsing,
and prompt text.
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
    acquire_lock, compute_cutoff, find_candidates, run_git, save_last_check, setup_logging,
)

MODEL = "claude-sonnet-5"
EFFORT = "high"
LABEL = "claude"
MARKER = "<!-- claude-automation -->"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_feature_issues.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_feature_issues.lock"
DEFAULT_STATE_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_feature_issues.state.json"

logger = logging.getLogger("handle_feature_issues")


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
    sections = [
        f"[ISSUE #{c['number']}] {c['url']} (reason: {c['reason']})\n"
        f"{c['title']}\n\n{c['body'] or '(empty body)'}"
        for c in candidates
    ]
    joined = "\n\n---\n\n".join(sections)
    return (
        "/handle-feature-issue\n\n"
        "The following GitHub issues are labeled 'claude', authored by the repo owner, and "
        "have new activity. Process each one per the command's rules - investigate its full "
        "comment/reaction history yourself, this list only tells you WHICH issues need "
        "attention, not WHAT changed. Do not re-scan the repo for other candidates, this list "
        "is already the full set:\n\n"
        f"{joined}"
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--max-turns", type=int, default=40)
    parser.add_argument("--since-hours", type=float, default=0.0,
                         help="Lookback window, hours component. Combines with --since-minutes; "
                              "if both are 0 (the default), falls back to a 1-hour window.")
    parser.add_argument("--since-minutes", type=float, default=0.0,
                         help="Lookback window, minutes component - use this alone for sub-hour "
                              "cron intervals, e.g. --since-minutes 15.")
    parser.add_argument("--log-file", type=Path, default=DEFAULT_LOG_FILE)
    parser.add_argument("--log-max-bytes", type=int, default=DEFAULT_LOG_MAX_BYTES)
    parser.add_argument("--log-backup-count", type=int, default=DEFAULT_LOG_BACKUP_COUNT)
    parser.add_argument("--lock-file", type=Path, default=DEFAULT_LOCK_FILE)
    parser.add_argument("--state-file", type=Path, default=DEFAULT_STATE_FILE,
                         help="Tracks the last completed discovery check, so a run skipped due "
                              "to lock contention doesn't shrink the effective lookback window "
                              "for the run after it.")
    args = parser.parse_args()

    setup_logging(logger, args.log_file, args.log_max_bytes, args.log_backup_count)

    lock = acquire_lock(logger, args.lock_file)
    if lock is None:
        logger.info("Another instance is already running - exiting. Not updating the last-check "
                     "timestamp, so the next run that acquires the lock still covers this window.")
        return

    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])

    now, cutoff = compute_cutoff(logger, args.state_file, args.since_hours, args.since_minutes)
    candidates = find_candidates(LABEL, MARKER, cutoff)

    if not candidates:
        logger.info(f"No '{LABEL}'-labeled issues with new activity since {cutoff.isoformat()} - nothing to do.")
        save_last_check(args.state_file, now)
        return

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
            summary = summarize_stream_event(json.loads(line))
        except json.JSONDecodeError:
            logger.info(f"[claude -p] {line}")
            continue
        if summary:
            logger.info(f"[claude -p] {summary}")
    process.wait()
    logger.info(f"claude -p exited with code {process.returncode}.")
    save_last_check(args.state_file, now)
    sys.exit(process.returncode)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_feature_issues.py failed")
        sys.exit(1)
