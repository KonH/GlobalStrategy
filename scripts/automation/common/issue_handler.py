"""Shared discovery/bookkeeping logic for the label-driven issue automations - the parts that
don't depend on which agent CLI (Claude Code or Codex) actually processes each item. Provider-
specific wrappers (scripts/automation/claude/handle_issues.py, scripts/automation/codex/
handle_issues.py) own everything about invoking their CLI and interpreting its output; this
module covers the identical remainder: process locking, log setup, candidate discovery, and
stale-run reclaim.

The label set IS the state machine - there is no local state file and no timestamp bookkeeping:

- `<label>` (e.g. `claude`)      - the owner opted this issue/PR in; its description (plus the
                                   owner's comments) is the prompt to execute.
- `<label>-in-progress`          - a run is actively working it; skipped by discovery.
- `<label>-needs-attention`      - the automation is waiting on the owner; skipped.
- `<label>-complete`             - the prompt is fully done; skipped.

An item is a candidate iff it carries `<label>` and none of the three status labels. The owner
resumes a needs-attention/complete item by replying and removing that status label - discovery
then selects it again with no further machinery.

Stale-run reclaim: the wrapper holds an exclusive process lock, so at run start no other run
can be active - any `<label>-in-progress` still on GitHub is by definition leftover from a
crashed/interrupted run. `reclaim_stale_in_progress` re-queues such items (removes the label)
up to MAX_AUTO_RECLAIMS times, counting attempts via `<marker>:reclaim` comments on the item
itself (GitHub is the counter - no local state); one more crash after that escalates to
`<label>-needs-attention` instead of retrying forever. A real owner comment resets the counter.

Repo: KonH/GlobalStrategy. This is project-specific, not provider-specific, so it lives here
rather than being duplicated per provider.
"""

import json
import logging
import subprocess
import sys
from logging.handlers import RotatingFileHandler

OWNER = "KonH"
REPO = "GlobalStrategy"
FIELDS = "number,title,body,url,labels"

# How many times a crashed/interrupted run is silently re-queued before the item is parked
# with `<label>-needs-attention` instead. 2 reclaims = 3 attempts total.
MAX_AUTO_RECLAIMS = 2


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


def reset_to_main(logger):
    """Hard-resets the dedicated automation clone to origin/main so the CLI run always starts
    from the current command/skill files, never a stale checkout. Local leftovers from a crashed
    run are deliberately discarded - the new-design rule is that every run commits and pushes
    even partial work, so anything only present locally was mid-crash churn the reclaimed retry
    will redo from the pushed branch state."""
    logger.info("Resetting checkout to origin/main.")
    run_git(["checkout", "main"])
    run_git(["fetch", "origin", "main"])
    run_git(["reset", "--hard", "origin/main"])


def run_git(args):
    result = subprocess.run(["git", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout.strip()


def run_gh(args):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"gh {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def run_gh_json(args):
    return json.loads(run_gh(args))


def add_label(number, name):
    # The issues REST endpoints cover PRs too, so one call shape works for both kinds.
    run_gh(["api", f"repos/{OWNER}/{REPO}/issues/{number}/labels", "-f", f"labels[]={name}"])


def remove_label(number, name):
    run_gh(["api", "-X", "DELETE", f"repos/{OWNER}/{REPO}/issues/{number}/labels/{name}"])


def post_comment(number, body):
    run_gh(["api", f"repos/{OWNER}/{REPO}/issues/{number}/comments", "-f", f"body={body}"])


def label_names(item):
    return {item_label["name"] for item_label in item.get("labels", [])}


def list_labeled_items(label):
    """All open, owner-authored issues AND pull requests carrying `label`. Each item gets a
    `kind` field ("issue"/"pr"); PRs also carry `headRefName` so the CLI run knows which branch
    to work on."""
    items = [
        {**issue, "kind": "issue"}
        for issue in run_gh_json([
            "issue", "list", "--repo", f"{OWNER}/{REPO}",
            "--label", label, "--author", OWNER, "--state", "open",
            "--json", FIELDS,
        ])
    ]
    items += [
        {**pr, "kind": "pr"}
        for pr in run_gh_json([
            "pr", "list", "--repo", f"{OWNER}/{REPO}",
            "--label", label, "--author", OWNER, "--state", "open",
            "--json", f"{FIELDS},headRefName",
        ])
    ]
    return items


def find_candidates(label):
    """An item is a candidate iff it carries `label` and none of the three status labels."""
    status_labels = {f"{label}-in-progress", f"{label}-needs-attention", f"{label}-complete"}
    return [item for item in list_labeled_items(label) if not (label_names(item) & status_labels)]


def count_reclaims_since_owner_comment(marker_prefix, reclaim_marker, number):
    """Counts consecutive reclaim-marker comments since the owner's last real (non-automation)
    comment on the item. The automation authenticates with the owner's own credentials, so
    author alone can't tell its comments apart from the owner's - the marker prefix is what
    distinguishes them. Comments from anyone else never reset the counter (the automation only
    ever acts on owner-authored content)."""
    comments = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{number}/comments", "--paginate"])
    count = 0
    for comment in comments:  # the API returns comments in chronological order
        body = comment.get("body", "")
        if body.startswith(reclaim_marker):
            count += 1
        elif comment.get("user", {}).get("login") == OWNER and not body.startswith(marker_prefix):
            count = 0
    return count


def reclaim_stale_in_progress(logger, label, marker):
    """Called at run start, after the process lock is held: no other run can be active, so any
    `<label>-in-progress` still on GitHub is leftover from a crashed/interrupted run. Re-queues
    it (removes the label so normal discovery selects it again) up to MAX_AUTO_RECLAIMS times;
    the crash after that escalates to `<label>-needs-attention` so repeated crashes never burn
    usage forever. Items already carrying `<label>-needs-attention` are genuinely waiting on
    the owner and are left untouched."""
    marker_prefix = marker.rsplit(" -->", 1)[0]
    reclaim_marker = f"{marker_prefix}:reclaim -->"
    in_progress_label = f"{label}-in-progress"
    needs_attention_label = f"{label}-needs-attention"
    for item in list_labeled_items(in_progress_label):
        number = item["number"]
        if needs_attention_label in label_names(item):
            continue
        reclaims = count_reclaims_since_owner_comment(marker_prefix, reclaim_marker, number)
        if reclaims >= MAX_AUTO_RECLAIMS:
            logger.warning(
                f"{item['kind']} #{number} still in-progress after {reclaims} automatic retries "
                f"- escalating to {needs_attention_label} instead of retrying again."
            )
            post_comment(number, (
                f"{marker}\nAutomated runs on this {item['kind']} were interrupted "
                f"{reclaims + 1} times in a row without finishing. Stopping automatic retries - "
                f"check the run logs, then reply with guidance and remove the "
                f"`{needs_attention_label}` label to try again."
            ))
            add_label(number, needs_attention_label)
            remove_label(number, in_progress_label)
        else:
            logger.warning(
                f"{item['kind']} #{number} still labeled {in_progress_label} - previous run "
                f"crashed or was interrupted; re-queuing (automatic retry {reclaims + 1} of "
                f"{MAX_AUTO_RECLAIMS})."
            )
            post_comment(number, (
                f"{reclaim_marker}\nA previous automated run was interrupted before finishing - "
                f"re-queuing this {item['kind']} (automatic retry {reclaims + 1} of "
                f"{MAX_AUTO_RECLAIMS})."
            ))
            remove_label(number, in_progress_label)
