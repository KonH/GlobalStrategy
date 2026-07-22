#!/usr/bin/env python3
"""Handle Codex Feature Issues - poll GitHub and drive approved feature issues to reviewable PRs.

Meant to run on a schedule (cron / Task Scheduler) in the user's own environment, not in a
CI runner or remote agent session - it uses the local `gh` and Codex CLI authentication on
the machine that owns this dedicated clone.

Cheap discovery, expensive work gated behind it: this script does the "is there anything to
do at all" check itself, via plain `gh` calls (no LLM usage). `codex exec` is only invoked
when discovery actually finds something.
Conversation happens entirely on the ISSUE (not the PR) across the whole spec -> plan ->
merge lifecycle, driven by the owner's comments and reactions - see
.codex/skills/codex-feature-issue/SKILL.md for the Codex state machine.

A candidate issue (open, labeled `codex`, authored by the owner) is picked up if either:
  - its `updatedAt` falls inside the lookback window (covers new issues and new comments), or
  - any reaction from the owner on one of this automation's own comments (identified by the
    `<!-- codex-automation -->` marker) has a `created_at` inside the window - reactions do
    NOT bump `updatedAt` on GitHub's side, so this needs its own separate check per open
    candidate, not just a timestamp filter on the issue list itself.

Single-instance lock: acquires an exclusive OS lock on Logs/handle_codex_feature_issues.lock
before doing anything else. If a previous run is still in flight when the next cron tick fires,
this run exits immediately instead of racing it - the lock releases automatically even if a
prior run crashed, since it's tied to the OS file descriptor, not manually cleared state.

Skipping a locked-out run must never cost a window of activity: the lookback cutoff is not
just "now minus --since-hours/--since-minutes", it's also clamped to the timestamp of the
last run that actually completed discovery (Logs/handle_codex_feature_issues.state.json). A run
that gets skipped because the lock is held simply never advances that timestamp, so the next
run that does acquire the lock looks back at least as far as the last successful check -
covering whatever activity happened during the skipped window - instead of the fixed rolling
window silently missing anything older than --since-minutes/--since-hours by the time it
finally runs.

Requires `gh` authenticated as the repo owner (`gh auth login`), the `codex` label already
created in the repo (`gh label create codex`), and `codex login status` succeeding. It uses
gpt-5.6-sol at high reasoning effort by default; override those values on the command line.

Logs to Logs/handle_codex_feature_issues.log (gitignored, same as Unity's own Logs/ folder) with
size-based auto-rotation - no unbounded append, no manual cleanup needed. Override with
--log-file/--log-max-bytes/--log-backup-count if you want it elsewhere.

Codex runs with `exec --json`; every event is written to the rotating log for diagnosis.

Usage (from project root):
  python scripts/automation/codex/handle_issues.py
  python scripts/automation/codex/handle_issues.py --since-hours 2
  python scripts/automation/codex/handle_issues.py --since-minutes 15 --model gpt-5.6-sol --effort high

Shared discovery/locking/state logic lives in scripts/automation/common/issue_handler.py - this
file only supplies what's specific to driving Codex: CLI invocation, exec --json event parsing,
and prompt text.
"""

import argparse
import json
import logging
import os
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent.parent))
from common.issue_handler import (  # noqa: E402
    acquire_lock, compute_cutoff, find_candidates, run_git, save_last_check, setup_logging,
)
from scripts.stats.collect_usage import record_usage_row_codex  # noqa: E402

USAGE_STAGE_RE = re.compile(r"^USAGE_STAGE:\s*(\S+)\s+(spec|plan)\s*$", re.MULTILINE)

MODEL = "gpt-5.6-sol"
EFFORT = "high"
DEFAULT_SANDBOX = "workspace-write"
SANDBOX_CHOICES = ["read-only", "workspace-write", "danger-full-access"]
LABEL = "codex"
MARKER = "<!-- codex-automation -->"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_codex_feature_issues.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_codex_feature_issues.lock"
DEFAULT_STATE_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_codex_feature_issues.state.json"
DEFAULT_GH_CONFIG_DIR = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "codex-gh-config"

logger = logging.getLogger("handle_codex_feature_issues")


def find_codex_executable():
    return shutil.which("codex") or "codex"


def build_codex_arguments(model, effort, sandbox):
    codex_args = [
        find_codex_executable(), "exec", "--json", "--sandbox", sandbox,
        "--config", "approval_policy=\"never\"",
        "--model", model,
        "--config", f'model_reasoning_effort=\"{effort}\"',
        "--ignore-user-config",
        "-",
    ]
    if sandbox == "workspace-write":
        codex_args[5:5] = ["--config", "sandbox_workspace_write.network_access=true"]
    return codex_args


def build_codex_environment():
    environment = os.environ.copy()
    token = environment.get("GH_ACCESS_TOKEN") or environment.get("GH_TOKEN") or environment.get("GITHUB_TOKEN")
    if token:
        environment["GH_TOKEN"] = token
    DEFAULT_GH_CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    environment["GH_CONFIG_DIR"] = str(DEFAULT_GH_CONFIG_DIR)
    environment["GIT_TERMINAL_PROMPT"] = "0"
    environment["GCM_INTERACTIVE"] = "Never"
    return environment


def build_prompt(candidates):
    sections = [
        f"[ISSUE #{c['number']}] {c['url']} (reason: {c['reason']})\n"
        f"{c['title']}\n\n{c['body'] or '(empty body)'}"
        for c in candidates
    ]
    joined = "\n\n---\n\n".join(sections)
    return (
        "Read and follow .codex/skills/codex-feature-issue/SKILL.md.\n\n"
        "The following GitHub issues are labeled 'codex', authored by the repo owner, and "
        "have new activity. Process each one per that skill - investigate its full "
        "comment/reaction history yourself, this list only tells you WHICH issues need "
        "attention, not WHAT changed. Do not re-scan the repo for other candidates, this list "
        "is already the full set. In your final agent message, end with exactly one line: "
        "AUTOMATION_RESULT: COMPLETED if every candidate reached its intended stopping point, "
        "or AUTOMATION_RESULT: BLOCKED if a missing prerequisite prevented that transition:\n\n"
        f"{joined}"
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", default=MODEL)
    parser.add_argument("--effort", default=EFFORT,
                        choices=["minimal", "low", "medium", "high", "xhigh"])
    parser.add_argument("--sandbox", default=DEFAULT_SANDBOX, choices=SANDBOX_CHOICES,
                        help="Codex sandbox mode. Use danger-full-access only on an isolated automation host.")
    parser.add_argument("--dangerously-skip-permissions", action="store_true",
                        help="Deprecated alias for --sandbox danger-full-access.")
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
    if args.dangerously_skip_permissions:
        args.sandbox = "danger-full-access"

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
    logger.info(f"Found {len(candidates)} candidate(s) - invoking codex exec.")

    run_start = datetime.now(timezone.utc).isoformat()
    codex_args = build_codex_arguments(args.model, args.effort, args.sandbox)
    process = subprocess.Popen(
        codex_args,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
        env=build_codex_environment(),
    )
    process.stdin.write(prompt)
    process.stdin.close()
    automation_result = None
    agent_messages = []
    for line in process.stdout:
        line = line.rstrip()
        if not line:
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            logger.info(f"[codex exec] {line}")
            continue
        item = event.get("item", {})
        if event.get("type") == "item.completed" and item.get("type") == "agent_message":
            text = item.get("text", "")
            agent_messages.append(text)
            match = re.search(r"^AUTOMATION_RESULT:\s*(COMPLETED|BLOCKED)\s*$", text, re.MULTILINE)
            if match:
                automation_result = match.group(1)
        logger.info("[codex exec] %s", json.dumps(event, ensure_ascii=False))
    process.wait()
    logger.info(f"codex exec exited with code {process.returncode}.")

    record_usage_stats_rows(agent_messages, run_start)

    if process.returncode != 0:
        sys.exit(process.returncode)
    if automation_result != "COMPLETED":
        logger.error("Codex did not complete the automation transition (result: %s). "
                     "Leaving the last-check timestamp unchanged for retry.", automation_result or "missing")
        sys.exit(1)
    save_last_check(args.state_file, now)
    sys.exit(0)


def record_usage_stats_rows(agent_messages, run_start):
    """Scans the run's agent messages for USAGE_STAGE markers (see
    .codex/skills/codex-feature-issue/SKILL.md) and records one usage.csv row per
    match, via the newest rollout file for this repo written since run_start - the
    wrapper has no finer-grained per-issue breakdown available, matching the
    acceptance of imprecision already accepted for multi-spec transcript segments
    elsewhere in this feature."""
    matches = USAGE_STAGE_RE.findall("\n".join(agent_messages))
    for spec_dir, stage in matches:
        try:
            record_usage_row_codex(spec_dir=spec_dir, stage=stage, mode="automated", since_iso=run_start)
        except Exception as error:  # usage-stats recording must never abort/fail the run
            logger.warning(f"failed to record usage stats row for {spec_dir}/{stage}: {error}")


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_codex_feature_issues.py failed")
        sys.exit(1)
