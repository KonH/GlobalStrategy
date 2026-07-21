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

Single-instance lock: acquires an exclusive flock on Logs/handle_codex_feature_issues.lock before
doing anything else. If a previous run is still in flight when the next cron tick fires, this
run exits immediately instead of racing it - the lock releases automatically even if a prior
run crashed, since it's tied to the OS file descriptor, not manually cleared state. (POSIX
only; on Windows, use Task Scheduler's own "don't start a new instance if already running".)

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
  python scripts/handle_codex_feature_issues.py
  python scripts/handle_codex_feature_issues.py --since-hours 2
  python scripts/handle_codex_feature_issues.py --since-minutes 15 --model gpt-5.6-sol --effort high
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

MODEL = "gpt-5.6-sol"
EFFORT = "high"
LABEL = "codex"
OWNER = "KonH"
REPO = "GlobalStrategy"
MARKER = "<!-- codex-automation -->"
FIELDS = "number,title,body,url,updatedAt"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent / "Logs" / "handle_codex_feature_issues.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent / "Logs" / "handle_codex_feature_issues.lock"
DEFAULT_STATE_FILE = Path(__file__).resolve().parent.parent / "Logs" / "handle_codex_feature_issues.state.json"

logger = logging.getLogger("handle_codex_feature_issues")


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


def load_last_check(state_file):
    if not state_file.exists():
        return None
    try:
        data = json.loads(state_file.read_text(encoding="utf-8"))
        return parse_timestamp(data["last_check_at"])
    except (ValueError, KeyError, json.JSONDecodeError):
        logger.warning(f"Could not parse {state_file} - ignoring stored last-check time.")
        return None


def save_last_check(state_file, when):
    state_file.parent.mkdir(parents=True, exist_ok=True)
    state_file.write_text(json.dumps({"last_check_at": when.isoformat()}), encoding="utf-8")


def find_codex_executable():
    return shutil.which("codex") or "codex"


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


def list_open_codex_issues():
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
    for issue in list_open_codex_issues():
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
        "Read and follow .codex/skills/codex-feature-issue/SKILL.md.\n\n"
        "The following GitHub issues are labeled 'codex', authored by the repo owner, and "
        "have new activity. Process each one per that skill - investigate its full "
        "comment/reaction history yourself, this list only tells you WHICH issues need "
        "attention, not WHAT changed. Do not re-scan the repo for other candidates, this list "
        "is already the full set:\n\n"
        f"{joined}"
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--model", default=MODEL)
    parser.add_argument("--effort", default=EFFORT,
                        choices=["minimal", "low", "medium", "high", "xhigh"])
    parser.add_argument("--dangerously-skip-permissions", action="store_true",
                        help="Bypass Codex sandboxing. Use only if the dedicated automation host is isolated.")
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

    setup_logging(args.log_file, args.log_max_bytes, args.log_backup_count)

    lock = acquire_lock(args.lock_file)
    if lock is None:
        logger.info("Another instance is already running - exiting. Not updating the last-check "
                     "timestamp, so the next run that acquires the lock still covers this window.")
        return

    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])

    now = datetime.now(timezone.utc)
    lookback_minutes = args.since_hours * 60 + args.since_minutes
    if lookback_minutes <= 0:
        lookback_minutes = 60
    window_cutoff = now - timedelta(minutes=lookback_minutes)
    last_check = load_last_check(args.state_file)
    cutoff = min(window_cutoff, last_check) if last_check else window_cutoff
    if last_check and last_check < window_cutoff:
        logger.info(f"Last completed check was {last_check.isoformat()}, older than the "
                     f"{lookback_minutes:g}m window - extending cutoff back to it so nothing "
                     "from a lock-skipped run is missed.")
    candidates = find_candidates(cutoff)

    if not candidates:
        logger.info(f"No '{LABEL}'-labeled issues with new activity since {cutoff.isoformat()} - nothing to do.")
        save_last_check(args.state_file, now)
        return

    prompt = build_prompt(candidates)
    logger.info(f"Found {len(candidates)} candidate(s) - invoking codex exec.")

    codex_args = [
        find_codex_executable(), "exec", "--json", "--sandbox", "workspace-write",
        "--config", "approval_policy=\"never\"",
        "--config", "sandbox_workspace_write.network_access=true",
        "--model", args.model,
        "--config", f'model_reasoning_effort=\"{args.effort}\"',
    ]
    if args.dangerously_skip_permissions:
        codex_args.append("--yolo")
    # Read the prompt from stdin rather than a Windows command-line argument. Issue bodies
    # can be large or contain characters that are hard to preserve through command-line parsing.
    codex_args.append("-")
    process = subprocess.Popen(
        codex_args,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        bufsize=1,
    )
    process.stdin.write(prompt)
    process.stdin.close()
    for line in process.stdout:
        line = line.rstrip()
        if not line:
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            logger.info(f"[codex exec] {line}")
            continue
        logger.info("[codex exec] %s", json.dumps(event, ensure_ascii=False))
    process.wait()
    logger.info(f"codex exec exited with code {process.returncode}.")
    save_last_check(args.state_file, now)
    sys.exit(process.returncode)


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_codex_feature_issues.py failed")
        sys.exit(1)
