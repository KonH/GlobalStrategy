"""Shared discovery/bookkeeping logic for the label-driven issue automations - the parts that
don't depend on which agent CLI (Claude Code or Codex) actually processes each item. Provider-
specific wrappers (scripts/automation/claude/handle_issues.py, scripts/automation/codex/
handle_issues.py) own everything about invoking their CLI and interpreting its output; this
module covers the identical remainder: process locking, log setup, candidate discovery, and
stale-run reclaim.

The label set IS the state machine - there is no local state file and no timestamp bookkeeping:

- `<provider>` (e.g. `claude`)   - the owner (or auto-router) opted this issue/PR in; its
                                   description (plus trusted comments) is the prompt to execute.
- `ai-in-progress`               - a run is actively working it; skipped by discovery.
- `ai-need-attention`            - the automation is waiting on the owner; skipped.
- `ai-complete`                  - the prompt is fully done; skipped.

Status labels are shared across providers. An item is a candidate iff it carries the provider
opt-in label and none of the three `ai-*` status labels. Reclaim/release only touch items that
also carry the calling provider's opt-in label, so one provider's lock cannot clear another's
in-flight work. The owner resumes a need-attention/complete item by replying and removing that
status label - discovery then selects it again with no further machinery.

Each candidate gets its own CLI invocation, started from a guaranteed-clean checkout of its
valid branch (`candidate_branch` + `checkout_clean`): main for an issue, the PR's head branch
for a PR. A batch can't share one checkout when items need different branches, so the wrapper
loops candidates rather than sending one combined prompt.

Stale-run reclaim: the wrapper holds an exclusive process lock, so at run start no other run
of *this* provider can be active - any `ai-in-progress` still on that provider's items is by
definition leftover from a crashed/interrupted run. `reclaim_stale_in_progress` re-queues such
items (removes the label) up to MAX_AUTO_RECLAIMS times, counting attempts via
`<marker>:reclaim` comments on the item itself (GitHub is the counter - no local state); one
more crash after that escalates to `ai-need-attention` instead of retrying forever. A real
owner comment resets the counter.

Session/usage limits are NOT crashes: when a wrapper detects that its CLI run was cut short by
the provider's session/usage limit, it first salvages any dirty working-tree changes
(`salvage_uncommitted_work` - deterministic Python git commit+push, no agent), records an
aware-UTC retry timestamp in a small local file, and then either silently releases the
`ai-in-progress` labels the interrupted run left behind (no reclaim comment, no crash-counter
increment - `release_in_progress_silently`) on a successful salvage, or parks the item with
`ai-need-attention` when salvage fails. A brief automation note is posted best-effort
*after* save/release so a failed `post_comment` cannot leave `ai-in-progress` for reclaim.
Every later run first checks `limit_active` - both sides of the comparison are timezone-aware
UTC datetimes, so the machine's local timezone never skews it - and exits immediately, before
any GitHub call, while the window is still in effect.

If the limit-hit item is still auto-routed (carries `auto-ai`), `handle_limit_pause` also calls
`reroute_auto_item_after_limit` right after a successful release: it drops the current provider
label and re-runs `select_auto_provider` over the remaining providers immediately, so the item
moves to a free provider instead of sitting out this provider's own backoff window while
siblings are idle. If every other provider is limited too, `park_auto_item_unroutable` applies
`ai-need-attention` and posts an automation note.

`checkout_clean` force-resets to `origin/<branch>`, but if the local branch already exists and
is ahead of its origin counterpart it pushes that tip first - so unpushed salvage commits are
not discarded. If that ahead-push fails, the force-reset is skipped (local tip preserved).

Repo: KonH/GlobalStrategy. This is project-specific, not provider-specific, so it lives here
rather than being duplicated per provider.
"""

import json
import logging
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from functools import lru_cache
from logging.handlers import RotatingFileHandler
from pathlib import Path

OWNER = "KonH"
REPO = "GlobalStrategy"
FIELDS = "number,title,body,url,labels"
AUTOMATION_ROOT = Path(__file__).resolve().parent.parent
CONTRIBUTORS_FILE = AUTOMATION_ROOT / "contributors.json"

AI_IN_PROGRESS = "ai-in-progress"
AI_NEED_ATTENTION = "ai-need-attention"
AI_COMPLETE = "ai-complete"
AI_STATUS_LABELS = frozenset({AI_IN_PROGRESS, AI_NEED_ATTENTION, AI_COMPLETE})

# Providers handle_issues_auto.py routes `auto-ai` items between. Defined here rather than in
# that module so handle_limit_pause (below) can reroute an auto-routed item to a different
# provider immediately on a limit hit, without an import cycle.
PROVIDERS = ("claude", "codex", "cursor")
AUTO_LABEL = "auto-ai"

# How many times a crashed/interrupted run is silently re-queued before the item is parked
# with `ai-need-attention` instead. 2 reclaims = 3 attempts total.
MAX_AUTO_RECLAIMS = 2

SALVAGE_COMMIT_MESSAGE = "chore: salvage uncommitted work after session limit"
SALVAGE_GIT_IDENTITY = {
    "GIT_AUTHOR_NAME": "GlobalStrategy Automation",
    "GIT_AUTHOR_EMAIL": "automation@local",
    "GIT_COMMITTER_NAME": "GlobalStrategy Automation",
    "GIT_COMMITTER_EMAIL": "automation@local",
}
AUTO_MARKER = "<!-- auto-ai-automation -->"


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


def candidate_branch(candidate):
    """The branch a candidate's CLI run must start from: a PR is worked on its existing head
    branch; an issue starts from main (the run creates/reuses its own feature branch from
    there)."""
    return candidate["headRefName"] if candidate["kind"] == "pr" else "main"


def local_branch_exists(branch):
    """True when `refs/heads/{branch}` exists locally. Does not raise on a missing ref."""
    result = subprocess.run(
        ["git", "show-ref", "--verify", "--quiet", f"refs/heads/{branch}"],
        capture_output=True,
        text=True,
    )
    return result.returncode == 0


def checkout_clean(logger, branch):
    """Guarantees a clean, up-to-date checkout of `branch` before a CLI run: the local branch
    is force-reset to its origin counterpart and untracked files are removed (`git clean -fd`
    keeps ignored files - Logs/, .venv/ - intact). If the local branch already exists and is
    ahead of `origin/{branch}`, it is pushed first so unpushed commits (e.g. a limit-pause
    salvage) are not discarded by the force-reset; a failed ahead-push raises without
    resetting over the local tip. Otherwise local leftovers from a previous run are
    deliberately discarded - every healthy run pushes what matters."""
    logger.info(f"Preparing clean checkout of '{branch}'.")
    run_git(["fetch", "origin", branch])
    if local_branch_exists(branch):
        ahead_count = int(run_git(["rev-list", "--count", f"origin/{branch}..{branch}"]) or "0")
        if ahead_count > 0:
            logger.info(f"Local '{branch}' is ahead of origin/{branch} by {ahead_count} "
                        "commit(s) - pushing before force-reset.")
            # If this push fails, do not force-reset over the local tip.
            run_git(["push", "-u", "origin", branch])
    run_git(["checkout", "-f", "-B", branch, f"origin/{branch}"])
    run_git(["clean", "-fd"])


def run_git(args, env=None):
    """Run a git command. Optional `env` is merged onto `os.environ` (e.g. GIT_AUTHOR_* for
    salvage commits); when omitted, the process environment is inherited unchanged."""
    run_env = None
    if env is not None:
        run_env = os.environ.copy()
        run_env.update(env)
    result = subprocess.run(["git", *args], capture_output=True, text=True, env=run_env)
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


def get_item_label_names(number):
    """Live label set for one issue/PR (issues API covers both)."""
    labels = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{number}/labels", "--paginate"])
    return {entry["name"] for entry in labels}


def verify_label_present(logger, number, name, attempts=3, initial_delay_seconds=1.0):
    """Wait briefly for `name` to show on the item after an add_label write.

    Up to `attempts` reads with exponential backoff between failures. Never raises for a
    missing label - logs and returns False so the caller can continue (next cron tick will
    rediscover). Used by auto-routing before provider handlers re-list by label.
    """
    delay = initial_delay_seconds
    for attempt in range(1, attempts + 1):
        try:
            if name in get_item_label_names(number):
                if attempt > 1:
                    logger.info("Verified label '%s' on #%s after %s attempt(s).",
                                name, number, attempt)
                return True
        except Exception as exc:
            logger.warning("Label verify read failed for #%s (%s) on attempt %s/%s: %s",
                           number, name, attempt, attempts, exc)
        if attempt < attempts:
            logger.info("Label '%s' not yet visible on #%s (attempt %s/%s); retrying in %ss.",
                        name, number, attempt, attempts, delay)
            time.sleep(delay)
            delay *= 2
    logger.warning("Label '%s' still not visible on #%s after %s attempts; continuing without "
                   "blocking the run.", name, number, attempts)
    return False


@lru_cache(maxsize=1)
def configured_contributors():
    """The GitHub accounts permitted to originate or refine automation work.

    A bad/missing config fails closed rather than broadening a scheduled automation's trust
    boundary. The repository owner remains the only initial configured contributor.
    """
    try:
        data = json.loads(CONTRIBUTORS_FILE.read_text(encoding="utf-8"))
        contributors = data["contributors"]
        if not isinstance(contributors, list) or not all(isinstance(name, str) and name for name in contributors):
            raise ValueError("contributors must be a non-empty list of GitHub login strings")
        return tuple(dict.fromkeys(contributors))
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as exc:
        logging.getLogger(__name__).error("Could not load contributor configuration %s: %s", CONTRIBUTORS_FILE, exc)
        return ()


def list_labeled_items(label):
    """All open, configured-contributor-authored issues AND pull requests carrying `label`. Each item gets a
    `kind` field ("issue"/"pr"); PRs also carry `headRefName` so the CLI run knows which branch
    to work on."""
    items = []
    for author in configured_contributors():
        items += [{**issue, "kind": "issue"} for issue in run_gh_json([
            "issue", "list", "--repo", f"{OWNER}/{REPO}", "--label", label,
            "--author", author, "--state", "open", "--json", FIELDS,
        ])]
    for author in configured_contributors():
        items += [{**pr, "kind": "pr"} for pr in run_gh_json([
            "pr", "list", "--repo", f"{OWNER}/{REPO}", "--label", label,
            "--author", author, "--state", "open", "--json", f"{FIELDS},headRefName",
        ])]
    return items


def find_candidates(label):
    """An item is a candidate iff it carries the provider `label` and none of the shared
    `ai-*` status labels."""
    return [item for item in list_labeled_items(label)
            if not (label_names(item) & AI_STATUS_LABELS)]


def _load_provider_state(logger, state_file):
    if not state_file.exists():
        return {"providers": {}}
    try:
        state = json.loads(state_file.read_text(encoding="utf-8"))
        if not isinstance(state, dict) or not isinstance(state.get("providers", {}), dict):
            raise ValueError("providers must be an object")
        return {"providers": state.get("providers", {})}
    except (OSError, ValueError, TypeError, json.JSONDecodeError) as exc:
        logger.warning("Could not parse %s - ignoring stored provider state: %s", state_file, exc)
        return {"providers": {}}


def _save_provider_state(state_file, state):
    state_file.parent.mkdir(parents=True, exist_ok=True)
    temporary_file = state_file.with_suffix(f"{state_file.suffix}.tmp")
    temporary_file.write_text(json.dumps(state, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary_file, state_file)


def load_limit_retry_at(logger, state_file, provider):
    """Returns the stored retry timestamp as an aware-UTC datetime, or None if the file is
    absent, the provider has no `limit_retry_at`, or the value is unreadable. A stored naive
    timestamp (shouldn't happen - save always writes an aware one) is defensively interpreted
    as UTC rather than local time. Missing keys are normal (provider may only have
    `last_auto_selection_at`) and must not warn; only a present but unparseable value warns."""
    data = _load_provider_state(logger, state_file)
    record = data["providers"].get(provider, {})
    if not isinstance(record, dict) or "limit_retry_at" not in record:
        return None
    value = record["limit_retry_at"]
    if value is None:
        return None
    try:
        retry_at = datetime.fromisoformat(value)
    except (ValueError, TypeError):
        logger.warning("Could not parse stored %s limit-retry time - ignoring it.", provider)
        return None
    if retry_at.tzinfo is None:
        retry_at = retry_at.replace(tzinfo=timezone.utc)
    return retry_at


def save_limit_retry_at(state_file, provider, retry_at):
    state = _load_provider_state(logging.getLogger(__name__), state_file)
    record = state["providers"].setdefault(provider, {})
    record["limit_retry_at"] = retry_at.astimezone(timezone.utc).isoformat()
    _save_provider_state(state_file, state)


def limit_active(logger, state_file, provider):
    """True while a previously recorded session/usage-limit window is still in effect - the
    caller must then skip the whole run (no GitHub calls, no CLI invocation). Both sides of
    the comparison are timezone-aware UTC datetimes, so the machine's local timezone never
    skews it. Once the window has passed, the file is removed and normal runs resume."""
    retry_at = load_limit_retry_at(logger, state_file, provider)
    if retry_at is None:
        return False
    now = datetime.now(timezone.utc)
    if now < retry_at:
        logger.info(f"Session/usage limit active until {retry_at.isoformat()} "
                     f"(now {now.isoformat()}) - skipping this run entirely.")
        return True
    logger.info(f"Recorded limit window expired at {retry_at.isoformat()} - resuming normal runs.")
    state = _load_provider_state(logger, state_file)
    record = state["providers"].get(provider, {})
    record.pop("limit_retry_at", None)
    if record:
        state["providers"][provider] = record
    else:
        state["providers"].pop(provider, None)
    _save_provider_state(state_file, state)
    return False


def select_auto_provider(logger, state_file, providers):
    """Return the eligible least-recently-auto-selected provider, or None.

    Stable input order breaks ties. Each caller must record the returned provider before making
    its next selection so a batch naturally rotates across providers.
    """
    eligible = [provider for provider in providers if not limit_active(logger, state_file, provider)]
    if not eligible:
        return None
    state = _load_provider_state(logger, state_file)

    def selection_time(provider):
        value = state["providers"].get(provider, {}).get("last_auto_selection_at")
        if not value:
            return datetime.min.replace(tzinfo=timezone.utc)
        try:
            parsed = datetime.fromisoformat(value)
            return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)
        except ValueError:
            logger.warning("Could not parse stored %s auto-selection time; treating it as never selected.", provider)
            return datetime.min.replace(tzinfo=timezone.utc)

    return min(eligible, key=selection_time)


def record_auto_selection(state_file, provider, selected_at=None):
    """Atomically persist an assignment before the next batch candidate is considered."""
    state = _load_provider_state(logging.getLogger(__name__), state_file)
    record = state["providers"].setdefault(provider, {})
    selected_at = selected_at or datetime.now(timezone.utc)
    record["last_auto_selection_at"] = selected_at.astimezone(timezone.utc).isoformat()
    _save_provider_state(state_file, state)


def release_in_progress_silently(logger, label):
    """Removes `ai-in-progress` from this provider's items a limit-interrupted run left behind,
    WITHOUT posting a reclaim marker comment: hitting the provider's session/usage limit is a
    planned pause, not a crash, so it must neither consume the crash-retry budget nor add error
    noise to the item's thread. The items return to plain candidates and are picked up again
    once the limit window passes. Items also carrying `ai-need-attention` stay untouched, same
    as in reclaim_stale_in_progress. Only items that also carry the provider opt-in `label` are
    touched, so shared status labels cannot clear another provider's in-flight work."""
    for item in list_labeled_items(label):
        names = label_names(item)
        if AI_IN_PROGRESS not in names:
            continue
        if AI_NEED_ATTENTION in names:
            continue
        logger.info(f"Releasing {item['kind']} #{item['number']} from {AI_IN_PROGRESS} "
                     "(limit pause, not a crash - no retry counted).")
        remove_label(item["number"], AI_IN_PROGRESS)


def salvage_uncommitted_work(logger):
    """Commit+push any dirty working-tree changes after a session/usage-limit kill so the next
    `checkout_clean` cannot wipe them. Deterministic Python git only - fixed message, no agent,
    no version bump. Returns `(status, detail)` where status is `clean`, `committed`, or
    `failed`."""
    try:
        porcelain = run_git(["status", "--porcelain"])
        if not porcelain:
            logger.info("Limit salvage: working tree clean - nothing to commit.")
            return "clean", "working tree clean"
        logger.info("Limit salvage: dirty tree - committing and pushing HEAD.")
        run_git(["add", "-A"])
        run_git(["commit", "-m", SALVAGE_COMMIT_MESSAGE], env=SALVAGE_GIT_IDENTITY)
        run_git(["push", "-u", "origin", "HEAD"])
        logger.info(f"Limit salvage: committed and pushed ({SALVAGE_COMMIT_MESSAGE}).")
        return "committed", SALVAGE_COMMIT_MESSAGE
    except Exception as exc:
        detail = str(exc)
        logger.warning(f"Limit salvage failed: {detail}")
        return "failed", detail


def park_auto_item_unroutable(logger, candidate):
    """Park an ``auto-ai`` item when no provider can take it.

    Applies ``ai-need-attention`` first so discovery skips it on the next tick, then posts a
    best-effort owner note. Keeps ``auto-ai`` so removing the status label resumes routing.
    """
    number = candidate["number"]
    kind = candidate.get("kind", "item")
    add_label(number, AI_NEED_ATTENTION)
    note = (
        f"{AUTO_MARKER}\nAll AI providers ({', '.join(PROVIDERS)}) currently have an active "
        f"usage/session limit, so this `{AUTO_LABEL}` {kind} could not be routed. Applied "
        f"`{AI_NEED_ATTENTION}`. Remove that label once a provider is available again to "
        "resume auto-routing."
    )
    try:
        post_comment(number, note)
    except Exception as exc:
        logger.warning("Failed to post all-providers-limited note on %s #%s: %s",
                       kind, number, exc)
    logger.warning("All providers are limited; parked %s #%s with %s.",
                   kind, number, AI_NEED_ATTENTION)


def reroute_auto_item_after_limit(logger, label, candidate, state_file):
    """After a limit-pause release, if `candidate` is still an auto-routed item (carries
    `auto-ai`), drop its current provider `label` and immediately pick a different eligible
    provider via `select_auto_provider` - instead of leaving it stuck until this provider's own
    backoff window passes while sibling providers sit idle. Uses the candidate's own cached
    label snapshot (already fetched by discovery this run), matching how `has_provider_label`
    in handle_issues_auto.py reads labels - no extra live GitHub call. Returns the newly
    selected provider, or None when the candidate wasn't auto-routed or every other provider is
    currently limited too (in the latter case the item is parked with `ai-need-attention`)."""
    if AUTO_LABEL not in label_names(candidate):
        return None
    number = candidate["number"]
    remove_label(number, label)
    other_providers = [provider for provider in PROVIDERS if provider != label]
    new_provider = select_auto_provider(logger, state_file, other_providers)
    if new_provider is None:
        park_auto_item_unroutable(logger, candidate)
        return None
    add_label(number, new_provider)
    record_auto_selection(state_file, new_provider)
    verify_label_present(logger, number, new_provider)
    logger.info(
        f"Rerouted {candidate['kind']} #{number} from '{label}' to '{new_provider}' "
        "immediately after a limit hit."
    )
    return new_provider


def handle_limit_pause(logger, label, marker, candidate, state_file, retry_at):
    """Shared limit-pause path for provider wrappers. Order is intentional: salvage,
    then always persist `retry_at`, then release-or-escalate labels, then best-effort note -
    so a failed `post_comment` cannot leave `ai-in-progress` for reclaim."""
    status, detail = salvage_uncommitted_work(logger)
    save_limit_retry_at(state_file, label, retry_at)
    number = candidate["number"]
    kind = candidate["kind"]
    retry_iso = retry_at.isoformat()

    if status in ("clean", "committed"):
        release_in_progress_silently(logger, label)
        new_provider = reroute_auto_item_after_limit(logger, label, candidate, state_file)
        if new_provider:
            note = (
                f"{marker}\nAutomation hit a session/usage limit on `{label}`. Salvage: {status}"
                f"{f' ({detail})' if detail else ''}. Rerouted to `{new_provider}` immediately "
                f"instead of waiting until {retry_iso}."
            )
        else:
            note = (
                f"{marker}\nAutomation hit a session/usage limit. Salvage: {status}"
                f"{f' ({detail})' if detail else ''}. Pausing until {retry_iso}."
            )
        try:
            post_comment(number, note)
        except Exception as exc:
            logger.warning(f"Failed to post limit-pause note on {kind} #{number}: {exc}")
    else:
        add_label(number, AI_NEED_ATTENTION)
        remove_label(number, AI_IN_PROGRESS)
        note = (
            f"{marker}\nAutomation hit a session/usage limit but failed to salvage "
            f"uncommitted work: {detail}. Applied `{AI_NEED_ATTENTION}`. "
            f"Pausing until {retry_iso}."
        )
        try:
            post_comment(number, note)
        except Exception as exc:
            logger.warning(f"Failed to post salvage-failure note on {kind} #{number}: {exc}")

    logger.warning(
        f"Session/usage limit hit - pausing runs until {retry_iso}. "
        f"Salvage status={status}. This is a planned pause, not a failure (exit 0)."
    )


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
        elif (comment.get("user", {}).get("login") in configured_contributors()
              and not body.startswith(marker_prefix)):
            count = 0
    return count


def reclaim_stale_in_progress(logger, label, marker):
    """Called at run start, after the process lock is held: no other run of this provider can
    be active, so any `ai-in-progress` still on this provider's items is leftover from a
    crashed/interrupted run. Re-queues it (removes the label so normal discovery selects it
    again) up to MAX_AUTO_RECLAIMS times; the crash after that escalates to
    `ai-need-attention` so repeated crashes never burn usage forever. Items already carrying
    `ai-need-attention` are genuinely waiting on the owner and are left untouched. Items that
    lack this provider's opt-in `label` are ignored so shared status labels stay provider-scoped
    under reclaim."""
    marker_prefix = marker.rsplit(" -->", 1)[0]
    reclaim_marker = f"{marker_prefix}:reclaim -->"
    for item in list_labeled_items(label):
        number = item["number"]
        names = label_names(item)
        if AI_IN_PROGRESS not in names:
            continue
        if AI_NEED_ATTENTION in names:
            continue
        reclaims = count_reclaims_since_owner_comment(marker_prefix, reclaim_marker, number)
        if reclaims >= MAX_AUTO_RECLAIMS:
            logger.warning(
                f"{item['kind']} #{number} still in-progress after {reclaims} automatic retries "
                f"- escalating to {AI_NEED_ATTENTION} instead of retrying again."
            )
            post_comment(number, (
                f"{marker}\nAutomated runs on this {item['kind']} were interrupted "
                f"{reclaims + 1} times in a row without finishing. Stopping automatic retries - "
                f"check the run logs, then reply with guidance and remove the "
                f"`{AI_NEED_ATTENTION}` label to try again."
            ))
            add_label(number, AI_NEED_ATTENTION)
            remove_label(number, AI_IN_PROGRESS)
        else:
            logger.warning(
                f"{item['kind']} #{number} still labeled {AI_IN_PROGRESS} - previous run "
                f"crashed or was interrupted; re-queuing (automatic retry {reclaims + 1} of "
                f"{MAX_AUTO_RECLAIMS})."
            )
            post_comment(number, (
                f"{reclaim_marker}\nA previous automated run was interrupted before finishing - "
                f"re-queuing this {item['kind']} (automatic retry {reclaims + 1} of "
                f"{MAX_AUTO_RECLAIMS})."
            ))
            remove_label(number, AI_IN_PROGRESS)
