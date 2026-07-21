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

Single-instance lock: acquires an exclusive flock on Logs/handle_feature_issues.lock before
doing anything else. If a previous run is still in flight when the next cron tick fires, this
run exits immediately instead of racing it - the lock releases automatically even if a prior
run crashed, since it's tied to the OS file descriptor, not manually cleared state. (POSIX
only; on Windows, use Task Scheduler's own "don't start a new instance if already running".)

Requires `gh` authenticated as the repo owner (`gh auth login`), the `claude` label already
created in the repo (`gh label create claude`), and `claude` logged into a subscription
(`claude` with no ANTHROPIC_API_KEY set - see .claude/rules/github_issue_automation.md for
why this matters). Runs explicitly on Sonnet 5 at high reasoning effort - unattended,
scheduled work with no one watching to catch a model/effort default drifting under it.

Logs to Logs/handle_feature_issues.log (gitignored, same as Unity's own Logs/ folder) with
size-based auto-rotation - no unbounded append, no manual cleanup needed. Override with
--log-file/--log-max-bytes/--log-backup-count if you want it elsewhere.

Usage (from project root):
  python scripts/handle_feature_issues.py
  python scripts/handle_feature_issues.py --since-hours 2 --max-turns 60
  python scripts/handle_feature_issues.py --since-minutes 15
"""

import argparse
import json
import logging
import shutil
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from logging.handlers import RotatingFileHandler
from pathlib import Path

MODEL = "claude-sonnet-5"
EFFORT = "high"
LABEL = "claude"
OWNER = "KonH"
REPO = "GlobalStrategy"
MARKER = "<!-- claude-automation -->"
FIELDS = "number,title,body,url,updatedAt"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent / "Logs" / "handle_feature_issues.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent / "Logs" / "handle_feature_issues.lock"

logger = logging.getLogger("handle_feature_issues")


def setup_logging(log_file, max_bytes, backup_count):
    log_file.parent.mkdir(parents=True, exist_ok=True)
    formatter = logging.Formatter("%(asctime)s %(levelname)s %(message)s")

    file_handler = RotatingFileHandler(log_file, maxBytes=max_bytes, backupCount=backup_count)
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)

    console_handler = logging.StreamHandler()
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    logger.setLevel(logging.INFO)


def acquire_lock(lock_file):
    lock_file.parent.mkdir(parents=True, exist_ok=True)
    lock_fp = open(lock_file, "w")
    try:
        import fcntl
    except ImportError:
        logger.info("fcntl unavailable (non-POSIX) - skipping process lock; rely on the "
                     "scheduler's own single-instance setting instead.")
        return lock_fp
    try:
        fcntl.flock(lock_fp, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        lock_fp.close()
        return None
    return lock_fp


def find_claude_executable():
    return shutil.which("claude") or "claude"


def run_git(args):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def run_gh_json(args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args)} failed: {result.stderr.strip()}")
    return json.loads(result.stdout)


def parse_timestamp(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def list_open_claude_issues():
    return run_gh_json([
        "issue", "list", "--repo", f"{OWNER}/{REPO}",
        "--label", LABEL, "--author", OWNER, "--state", "open",
        "--json", FIELDS,
    ])


def has_recent_owner_reaction(issue_number, cutoff):
    comments = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{issue_number}/comments"])
    for comment in comments:
        if not comment.get("body", "").startswith(MARKER):
            continue
        reactions = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/comments/{comment['id']}/reactions"])
        for reaction in reactions:
            if reaction["user"]["login"] == OWNER and parse_timestamp(reaction["created_at"]) >= cutoff:
                return True
    return False


def find_candidates(cutoff):
    candidates = []
    for issue in list_open_claude_issues():
        if parse_timestamp(issue["updatedAt"]) >= cutoff:
            candidates.append({**issue, "reason": "issue/comment updated"})
        elif has_recent_owner_reaction(issue["number"], cutoff):
            candidates.append({**issue, "reason": "new reaction on a summary/conclusion comment"})
    return candidates


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
    args = parser.parse_args()

    setup_logging(args.log_file, args.log_max_bytes, args.log_backup_count)

    lock = acquire_lock(args.lock_file)
    if lock is None:
        logger.info("Another instance is already running - exiting.")
        return

    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])

    lookback_minutes = args.since_hours * 60 + args.since_minutes
    if lookback_minutes <= 0:
        lookback_minutes = 60
    cutoff = datetime.now(timezone.utc) - timedelta(minutes=lookback_minutes)
    candidates = find_candidates(cutoff)

    if not candidates:
        logger.info(f"No '{LABEL}'-labeled issues with new activity in the last {lookback_minutes:g}m - nothing to do.")
        return

    prompt = build_prompt(candidates)
    logger.info(f"Found {len(candidates)} candidate(s) - invoking claude -p.")

    claude_exe = find_claude_executable()
    result = subprocess.run([
        claude_exe, "-p", prompt,
        "--model", MODEL,
        "--effort", EFFORT,
        "--dangerously-skip-permissions",
        "--max-turns", str(args.max_turns),
    ])
    logger.info(f"claude -p exited with code {result.returncode}.")
    sys.exit(result.returncode)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_feature_issues.py failed")
        sys.exit(1)
