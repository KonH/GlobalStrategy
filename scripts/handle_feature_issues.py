#!/usr/bin/env python3
"""Handle Feature Issues - polls GitHub for owner-authored feature issues and drafts specs.

Meant to run on a schedule (cron / Task Scheduler) in the user's own environment, not in a
CI runner or Claude Code Remote session - it uses whatever `gh` auth and `claude` login
(subscription-based, not an API key) already exist on the machine it runs on.

Flow per invocation:
  1. Switch to `main`, pull latest (so it always runs the current `.claude/commands/
     handle-feature-issue.md` and never drifts from a stale local checkout).
  2. Run `claude -p "/handle-feature-issue"` once - all the actual polling/classification/
     spec-writing/PR/comment logic lives in that command file, not here. This wrapper only
     owns "make sure the repo is current, then invoke Claude."

Requires `gh` authenticated as the repo owner (`gh auth login`) and `claude` logged into a
subscription (`claude` with no ANTHROPIC_API_KEY set - see .claude/rules/
github_issue_automation.md for why this matters).

Usage (from project root):
  python scripts/handle_feature_issues.py
  python scripts/handle_feature_issues.py --max-turns 60
"""

import argparse
import shutil
import subprocess
import sys


def find_claude_executable():
    return shutil.which("claude") or "claude"


def run_git(args):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--max-turns", type=int, default=40)
    args = parser.parse_args()

    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])

    claude_exe = find_claude_executable()
    result = subprocess.run([
        claude_exe, "-p", "/handle-feature-issue",
        "--dangerously-skip-permissions",
        "--max-turns", str(args.max_turns),
    ])
    sys.exit(result.returncode)


if __name__ == "__main__":
    main()
