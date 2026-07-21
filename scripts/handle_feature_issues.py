#!/usr/bin/env python3
"""Handle Feature Issues - polls GitHub for owner-authored feature issues and drafts specs.

Meant to run on a schedule (cron / Task Scheduler) in the user's own environment, not in a
CI runner or Claude Code Remote session - it uses whatever `gh` auth and `claude` login
(subscription-based, not an API key) already exist on the machine it runs on.

Cheap discovery, expensive work gated behind it: this script does the "is there anything to
do at all" check itself, via plain `gh` calls (no LLM usage). `claude -p` is only invoked -
and only then does it spend subscription usage - when at least one issue/PR labeled `claude`
was created or updated within the lookback window (`--since-hours` / `--since-minutes`,
default 1h, meant to match the cron interval). An empty poll costs nothing.

Flow per invocation:
  1. Switch to `main`, pull latest (so it always runs the current `.claude/commands/
     handle-feature-issue.md` and never drifts from a stale local checkout).
  2. `gh issue list` / `gh pr list`, both filtered to `--label claude`, to find candidates.
  3. Keep only candidates whose `updatedAt` falls inside the lookback window.
  4. If none: exit, no `claude -p` call made.
  5. If any: invoke `claude -p "/handle-feature-issue ..."` once, with each candidate's
     link + content embedded directly in the prompt - the command's own classification/
     spec-writing/PR/comment logic then takes it from there.

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
FIELDS = "number,title,body,url,updatedAt"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent / "Logs" / "handle_feature_issues.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk

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


def find_claude_executable():
    return shutil.which("claude") or "claude"


def run_git(args):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def run_gh_json(args):
    result = subprocess.run(["gh", *args, "--json", FIELDS], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args)} failed: {result.stderr.strip()}")
    return json.loads(result.stdout)


def parse_updated_at(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def find_candidates(kind, cutoff):
    items = run_gh_json([kind, "list", "--label", LABEL, "--state", "open"])
    return [
        {**item, "kind": "issue" if kind == "issue" else "pr"}
        for item in items
        if parse_updated_at(item["updatedAt"]) >= cutoff
    ]


def build_prompt(candidates):
    sections = [
        f"[{c['kind'].upper()} #{c['number']}] {c['url']} (updated {c['updatedAt']})\n"
        f"{c['title']}\n\n{c['body'] or '(empty body)'}"
        for c in candidates
    ]
    joined = "\n\n---\n\n".join(sections)
    return (
        "/handle-feature-issue\n\n"
        "The following GitHub issues/PRs are labeled 'claude' and were created or updated "
        "within the lookback window. Process each one per the command's rules - do not "
        "re-scan the whole repo for other candidates, this list is already the full set:\n\n"
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
    args = parser.parse_args()

    setup_logging(args.log_file, args.log_max_bytes, args.log_backup_count)

    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])

    lookback_minutes = args.since_hours * 60 + args.since_minutes
    if lookback_minutes <= 0:
        lookback_minutes = 60
    cutoff = datetime.now(timezone.utc) - timedelta(minutes=lookback_minutes)
    candidates = find_candidates("issue", cutoff) + find_candidates("pr", cutoff)

    if not candidates:
        logger.info(f"No '{LABEL}'-labeled issues/PRs updated in the last {lookback_minutes:g}m - nothing to do.")
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
