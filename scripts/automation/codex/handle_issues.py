#!/usr/bin/env python3
"""Handle Labeled Issues (Codex) - polls GitHub and executes the owner's prompts from
`codex`-labeled issues and PRs.

Meant to run on a schedule (cron / Task Scheduler) in the user's own environment, not in a
CI runner or remote agent session - it uses the local `gh` and Codex CLI authentication on
the machine that owns this dedicated clone.

Cheap discovery, expensive work gated behind it: this script does the "is there anything to
do at all" check itself, via plain `gh` calls (no LLM usage). `codex exec` is only invoked
when discovery actually finds something.

The label set is the whole state machine (no local state file, no timestamps):

  codex                  the owner opted an issue/PR in; its description + owner comments
                         are the prompt to execute
  codex-in-progress      a run is actively working it (skipped by discovery)
  codex-needs-attention  waiting on the owner (skipped by discovery)
  codex-complete         prompt fully done (skipped by discovery)

A candidate is any open, owner-authored, `codex`-labeled issue/PR carrying none of the three
status labels. The owner resumes a needs-attention/complete item by replying and removing that
label. See .codex/skills/codex-issue/SKILL.md for the per-item lifecycle the CLI run follows.

Each candidate gets its own `codex exec` invocation, started from a guaranteed-clean checkout
of its valid branch: `main` for an issue (force-reset to origin/main, untracked files
removed), the PR's head branch for a PR. The script prepares that checkout itself, so the CLI
run never starts from a stale or dirty tree - and a batch of candidates needing different
branches can't contaminate each other.

Single-instance lock: acquires an exclusive OS lock on Logs/handle_issues_codex.lock before
doing anything else. If a previous run is still in flight when the next cron tick fires, this
run exits immediately instead of racing it - the lock releases automatically even if a prior
run crashed, since it's tied to the OS file descriptor, not manually cleared state.

Stale-run reclaim: because the lock guarantees no other run is active, any item still labeled
`codex-in-progress` at startup is leftover from a crashed/interrupted run. It's re-queued (the
label removed, a `<!-- codex-automation:reclaim -->` comment posted as the attempt counter) up
to twice; a third consecutive crash parks it with `codex-needs-attention` instead of retrying
forever. A real owner comment resets the counter.

Usage limits are NOT crashes: when the run's error output reports a usage/rate limit, this
script records a retry time (now + --limit-backoff-minutes; Codex reports no machine-readable
reset time) as an aware-UTC timestamp in Logs/handle_issues_codex.limit.json, silently removes
the `codex-in-progress` labels the interrupted run left behind (no reclaim comment, no
crash-retry consumed, no error noise on the item), and exits 0. Every later run compares that
timestamp against the current time - both aware UTC, so the machine's local timezone never
skews it - and skips entirely (before any GitHub call) until the window has passed.

Requires `gh` authenticated as the repo owner (`gh auth login`), the labels above already
created in the repo (see handle_issues.sh for the `gh label create` commands), and
`codex login status` succeeding. It uses gpt-5.6-sol at high reasoning effort by default;
override those values on the command line.

Logs to Logs/handle_issues_codex.log (gitignored, same as Unity's own Logs/ folder) with
size-based auto-rotation - no unbounded append, no manual cleanup needed. Override with
--log-file/--log-max-bytes/--log-backup-count if you want it elsewhere.

Codex runs with `exec --json`; every event is written to the rotating log for diagnosis.

Usage (from project root):
  python scripts/automation/codex/handle_issues.py
  python scripts/automation/codex/handle_issues.py --model gpt-5.6-sol --effort high

Shared discovery/locking/reclaim logic lives in scripts/automation/common/issue_handler.py -
this file only supplies what's specific to driving Codex: CLI invocation, exec --json event
parsing, and prompt text.
"""

import argparse
import json
import logging
import os
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

MODEL = "gpt-5.6-sol"
EFFORT = "high"
DEFAULT_SANDBOX = "workspace-write"
SANDBOX_CHOICES = ["read-only", "workspace-write", "danger-full-access"]
LABEL = "codex"
MARKER = "<!-- codex-automation -->"

DEFAULT_LOG_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_codex.log"
DEFAULT_LOG_MAX_BYTES = 5 * 1024 * 1024  # 5 MB per file
DEFAULT_LOG_BACKUP_COUNT = 5  # + the active file = 30 MB max on disk
DEFAULT_LOCK_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_codex.lock"
DEFAULT_GH_CONFIG_DIR = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "codex-gh-config"
DEFAULT_LIMIT_FILE = Path(__file__).resolve().parent.parent.parent.parent / "Logs" / "handle_issues_codex.limit.json"
DEFAULT_LIMIT_BACKOFF_MINUTES = 60

logger = logging.getLogger("handle_issues_codex")

# Codex reports subscription/usage limits in error/failed events or plain-text output (no
# machine-readable reset time). Detection deliberately looks ONLY at error output (non-JSON
# lines and error/failed events), never at agent/tool text - an issue whose prompt merely
# talks about usage limits must not trigger a false positive.
LIMIT_TEXT_RE = re.compile(r"(usage limit|rate limit|quota exceeded)", re.IGNORECASE)


def detect_session_limit(error_texts):
    return bool(LIMIT_TEXT_RE.search("\n".join(error_texts)))


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
        "Read and follow .codex/skills/codex-issue/SKILL.md.\n\n"
        f"The following GitHub {kind.lower()} is labeled 'codex', authored by the repo owner, "
        "and carries no automation status label. Process it per that skill. The working tree "
        f"is already a guaranteed-clean, up-to-date checkout of {checkout}. The description "
        "shown here may be stale - re-read the item's live description and comment thread "
        "yourself. Do not process any other issues or PRs, this item is this run's only "
        "candidate:\n\n"
        f"{header}\n{candidate['title']}\n\n{candidate['body'] or '(empty body)'}"
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
    parser.add_argument("--log-file", type=Path, default=DEFAULT_LOG_FILE)
    parser.add_argument("--log-max-bytes", type=int, default=DEFAULT_LOG_MAX_BYTES)
    parser.add_argument("--log-backup-count", type=int, default=DEFAULT_LOG_BACKUP_COUNT)
    parser.add_argument("--lock-file", type=Path, default=DEFAULT_LOCK_FILE)
    parser.add_argument("--limit-file", type=Path, default=DEFAULT_LIMIT_FILE,
                        help="Stores the aware-UTC timestamp until which runs are skipped "
                             "after a usage-limit hit.")
    parser.add_argument("--limit-backoff-minutes", type=int, default=DEFAULT_LIMIT_BACKOFF_MINUTES,
                        help="How long to pause after a usage-limit hit (Codex reports no "
                             "machine-readable reset time).")
    args = parser.parse_args()
    if args.dangerously_skip_permissions:
        args.sandbox = "danger-full-access"

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
                    f"'{branch}' - invoking codex exec.")
        returncode, error_texts = run_codex(build_prompt(candidate), args)
        if returncode != 0:
            exit_code = returncode
        if detect_session_limit(error_texts):
            retry_at = datetime.now(timezone.utc) + timedelta(minutes=args.limit_backoff_minutes)
            save_limit_retry_at(args.limit_file, retry_at)
            release_in_progress_silently(logger, LABEL)
            logger.warning(f"Usage limit hit - pausing runs until {retry_at.isoformat()}. This "
                           "is a planned pause, not a failure (exit 0); interrupted and "
                           "remaining items stay in the candidate pool without consuming a "
                           "crash retry.")
            sys.exit(0)
    sys.exit(exit_code)


def run_codex(prompt, args):
    """Runs one codex exec invocation to completion, streaming its events into the log.
    Returns (returncode, error_texts) - error_texts is the CLI's error output only (non-JSON
    lines plus error/failed events), the input for usage-limit detection."""
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
    error_texts = []
    for line in process.stdout:
        line = line.rstrip()
        if not line:
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            error_texts.append(line)  # non-JSON output = CLI error text, e.g. a limit message
            logger.info(f"[codex exec] {line}")
            continue
        event_type = str(event.get("type", ""))
        if "error" in event_type or "failed" in event_type:
            error_texts.append(json.dumps(event, ensure_ascii=False))
        logger.info("[codex exec] %s", json.dumps(event, ensure_ascii=False))
    process.wait()
    logger.info(f"codex exec exited with code {process.returncode}.")
    return process.returncode, error_texts


if __name__ == "__main__":
    try:
        main()
    except Exception:
        logger.exception("handle_issues.py failed")
        sys.exit(1)
