"""Shared discovery/bookkeeping logic for the feature-issue automations - the parts that don't
depend on which agent CLI (Claude Code or Codex) actually processes each issue. Provider-specific
wrappers (scripts/automation/claude/handle_issues.py, scripts/automation/codex/handle_issues.py)
own everything about invoking their CLI and interpreting its result (that differs enough between
providers - stream-json parsing vs. exec --json event parsing, an AUTOMATION_RESULT convention
Codex requires and Claude doesn't, etc. - that forcing it through a shared orchestrator would cost
more clarity than it saves); this module covers the identical remainder: process locking, log
setup, the lookback-window/state-file cutoff computation, and GitHub candidate discovery.

Repo: KonH/GlobalStrategy. This is project-specific, not provider-specific, so it lives here
rather than being duplicated per provider.
"""

import json
import logging
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from logging.handlers import RotatingFileHandler

OWNER = "KonH"
REPO = "GlobalStrategy"
FIELDS = "number,title,body,url,updatedAt"


def setup_logging(logger, log_file, max_bytes, backup_count):
    log_file.parent.mkdir(parents=True, exist_ok=True)
    formatter = logging.Formatter("%(asctime)s %(levelname)s %(message)s")

    file_handler = RotatingFileHandler(log_file, maxBytes=max_bytes, backupCount=backup_count)
    file_handler.setFormatter(formatter)
    logger.addHandler(file_handler)

    console_handler = logging.StreamHandler()
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    logger.setLevel(logging.INFO)


def acquire_lock(logger, lock_file):
    """Exclusive, non-blocking, cross-platform process lock. Uses msvcrt on Windows and fcntl on
    POSIX; on a platform with neither, logs a warning and returns a live (unlocked) handle so the
    caller still has a file object, relying on the OS scheduler's own single-instance option
    instead (e.g. Windows Task Scheduler's "don't start a new instance if already running")."""
    lock_file.parent.mkdir(parents=True, exist_ok=True)
    lock_fp = open(lock_file, "a+b")
    lock_fp.seek(0, 2)
    if lock_fp.tell() == 0:
        lock_fp.write(b"\0")
        lock_fp.flush()
    lock_fp.seek(0)

    if sys.platform == "win32":
        import msvcrt
        try:
            msvcrt.locking(lock_fp.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            lock_fp.close()
            return None
        return lock_fp

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


def load_last_check(logger, state_file):
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


def compute_cutoff(logger, state_file, since_hours, since_minutes):
    """Combines the --since-hours/--since-minutes lookback window (defaulting to 1h if both are
    0) with the timestamp of the last run that actually completed discovery, so a run skipped due
    to lock contention doesn't shrink the effective lookback window for the run after it. Returns
    (now, cutoff)."""
    now = datetime.now(timezone.utc)
    lookback_minutes = since_hours * 60 + since_minutes
    if lookback_minutes <= 0:
        lookback_minutes = 60
    window_cutoff = now - timedelta(minutes=lookback_minutes)
    last_check = load_last_check(logger, state_file)
    cutoff = min(window_cutoff, last_check) if last_check else window_cutoff
    if last_check and last_check < window_cutoff:
        logger.info(f"Last completed check was {last_check.isoformat()}, older than the "
                     f"{lookback_minutes:g}m window - extending cutoff back to it so nothing "
                     "from a lock-skipped run is missed.")
    return now, cutoff


def run_git(args):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def reset_to_main_unless_in_progress(logger):
    """Hard-resets the local checkout to origin/main, unless some issue is still labeled
    `claude-in-progress` on GitHub - the marker handle-feature-issue.md applies at the start of
    handling an issue and removes when done (see its "Concurrency label" section). A label left
    on means the previous run crashed or was interrupted mid-issue (e.g. a Ralph loop from
    section 4b) before it could commit/push its work-in-progress and remove the label; forcibly
    resetting in that state would discard that work. Every code path in handle-feature-issue.md
    already does its own git fetch/checkout as its first step, so skipping this wrapper-level
    reset is safe - it's a cleanliness convenience for the common case, not something the
    command relies on. Any other local dirtiness (e.g. changes left behind after an issue
    completed and the label was cleared) is not treated as a reason to skip - only an
    in-progress label means a run is still mid-flight."""
    in_progress = run_gh_json([
        "issue", "list", "--repo", f"{OWNER}/{REPO}",
        "--label", "claude-in-progress", "--state", "open",
        "--json", "number",
    ])
    if in_progress:
        numbers = ", ".join(f"#{issue['number']}" for issue in in_progress)
        logger.warning(
            f"Issue(s) {numbers} still labeled 'claude-in-progress' - skipping the local reset "
            "to origin/main. This looks like a previous run's issue handling failed or was "
            "interrupted before finishing and clearing the label; leaving the checkout as-is so "
            "nothing is discarded. Investigate manually if this persists across runs."
        )
        return
    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])


def run_gh_json(args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args)} failed: {result.stderr.strip()}")
    return json.loads(result.stdout)


def parse_timestamp(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def list_open_issues(label):
    return run_gh_json([
        "issue", "list", "--repo", f"{OWNER}/{REPO}",
        "--label", label, "--author", OWNER, "--state", "open",
        "--json", FIELDS,
    ])


def has_recent_owner_reaction(marker, issue_number, cutoff):
    comments = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{issue_number}/comments"])
    for comment in comments:
        if not comment.get("body", "").startswith(marker):
            continue
        reactions = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/comments/{comment['id']}/reactions"])
        for reaction in reactions:
            if reaction["user"]["login"] == OWNER and parse_timestamp(reaction["created_at"]) >= cutoff:
                return True
    return False


def has_new_owner_activity(bot_comment_prefix, bot_status_labels, issue_number, cutoff):
    """Timeline-based replacement for a blunt `updatedAt >= cutoff` check. `updatedAt` also
    moves on events the automation caused itself in its own previous run - adding/removing its
    `<label>-in-progress`/`<label>-needs-attention` status labels, posting a marker-prefixed
    summary/checklist comment - which would otherwise make every run re-trigger itself on the
    very next cycle even with no real owner activity. Only a comment that isn't one of the
    automation's own marker comments, or a labeled/unlabeled event for a label outside its own
    status labels, is filtered out - the automation never renames issues itself, so `renamed`
    (and any other event type) still counts as real activity, same as before this filtering was
    added."""
    events = run_gh_json([
        "api", f"repos/{OWNER}/{REPO}/issues/{issue_number}/timeline", "--paginate",
    ])
    for event in events:
        timestamp = event.get("created_at")
        if not timestamp or parse_timestamp(timestamp) < cutoff:
            continue
        event_type = event.get("event")
        if event_type == "commented":
            if not event.get("body", "").startswith(bot_comment_prefix):
                return True
        elif event_type in ("labeled", "unlabeled"):
            label_name = (event.get("label") or {}).get("name", "")
            if label_name not in bot_status_labels:
                return True
        else:
            return True
    return False


def find_candidates(label, marker, cutoff):
    bot_status_labels = {f"{label}-in-progress", f"{label}-needs-attention"}
    bot_comment_prefix = marker.rsplit(" -->", 1)[0]
    candidates = []
    for issue in list_open_issues(label):
        if parse_timestamp(issue["updatedAt"]) < cutoff:
            if has_recent_owner_reaction(marker, issue["number"], cutoff):
                candidates.append({**issue, "reason": "new reaction on a summary/conclusion comment"})
            continue
        if has_new_owner_activity(bot_comment_prefix, bot_status_labels, issue["number"], cutoff):
            candidates.append({**issue, "reason": "issue/comment updated"})
        elif has_recent_owner_reaction(marker, issue["number"], cutoff):
            candidates.append({**issue, "reason": "new reaction on a summary/conclusion comment"})
    return candidates
