"""Shared discovery/bookkeeping logic for the label-driven issue automations - the parts that
don't depend on which agent CLI (Claude Code, Codex, or Cursor) actually processes each item.
Provider-specific wrappers (scripts/automation/claude|codex|cursor/handle_issues.py) own
everything about invoking their CLI and interpreting its output; this module covers the
identical remainder: process locking, log setup, candidate discovery, cross-instance claim,
and post-CLI / limit-path clear of `ai-in-progress`.

The label set IS the state machine - there is no local state file and no timestamp bookkeeping:

- `<provider>` (e.g. `claude`)   - the owner (or auto-router) opted this issue/PR in; its
                                   description (plus trusted comments) is the prompt to execute.
- `ai-in-progress`               - a run is actively working it; skipped by discovery.
- `ai-need-attention`            - the automation is waiting on the owner; skipped.
- `ai-complete`                  - the prompt is fully done; skipped.
- `ai-specify` / `ai-plan` /
  `ai-implement`                 - informational stage progress while an agent runs
                                   `/specify`, `/plan`, or `/implement`; not discovery
                                   status (do not skip candidates). Agents set these.

Status labels (`ai-in-progress` / `ai-need-attention` / `ai-complete`) are shared across
providers. An item is a candidate iff it carries the provider opt-in label and none of those
three status labels. Stage labels do not affect discovery. The Python wrappers alone add and
remove `ai-in-progress` (via `claim_candidate` before work and `clear_in_progress` after CLI
return / limit paths). Agents never touch that label. The owner resumes a need-attention/
complete item by replying and removing that status label - discovery then selects it again
with no further machinery.

Each candidate gets its own CLI invocation, started from a guaranteed-clean checkout of its
valid branch (`candidate_branch` + `checkout_clean`): main for an issue, the PR's head branch
for a PR. A batch can't share one checkout when items need different branches, so the wrapper
loops candidates rather than sending one combined prompt.

Cross-instance claim: the local process lock only serializes one provider's own loop on one
machine - it does nothing when two separate automation instances (different machines, or
overlapping schedules) discover the same unlabeled candidate within the same short window, since
a plain `add_label` call is not compare-and-swap. `claim_candidate` is called per candidate,
immediately after discovery and before any git/CLI work, to close that gap: it adds
`ai-in-progress` (idempotent) and posts a uniquely-tokened, fully invisible claim comment, waits
a short settle delay so both sides of a genuine race have time to post, then re-lists the item's
claim comments and lets whichever one has the lowest (i.e. earliest-created) comment id win -
GitHub comment ids are a monotonic server-assigned tie-break, unlike `created_at`'s coarse
per-second resolution. Only comments created within the last `freshness_minutes` count, so a
crash between posting a claim and cleaning it up can never permanently deadlock the item. The
winner best-effort deletes every fresh claim comment (`delete_comment`); the loser does nothing
further, leaving the winner's label and markers untouched.

Harness-owned clear: after a successful claim, wrappers wrap checkout + CLI + limit/max-turns
handling in `try`/`finally: clear_in_progress(logger, number)` so success, non-limit failure,
Cursor `SystemExit`, and Claude `error_max_turns` all drop `ai-in-progress` for that claimed
item only. Hard-kill before that cleanup can leave the label stuck; recovery is manual owner
removal of `ai-in-progress` on GitHub - there is no automatic stale-label reclaim (peer instances
must not strip a live run's claim).

Session/usage limits are NOT crashes: when a wrapper detects that its CLI run was cut short by
the provider's session/usage limit, it first salvages any dirty working-tree changes
(`salvage_uncommitted_work` - deterministic Python git commit+push, no agent), records an
aware-UTC retry timestamp in a small local file, and then either clears `ai-in-progress` for
the claimed item only (`clear_in_progress`) on a successful salvage, or parks the item with
`ai-need-attention` when salvage fails. A brief automation note is posted best-effort
*after* save/release so a failed `post_comment` cannot leave the label stuck when release already
succeeded. Every later run first checks `limit_active` - both sides of the comparison are
timezone-aware UTC datetimes, so the machine's local timezone never skews it - and exits
immediately, before any GitHub call, while the window is still in effect.

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
import uuid
from datetime import datetime, timedelta, timezone
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
AI_SPECIFY = "ai-specify"
AI_PLAN = "ai-plan"
AI_IMPLEMENT = "ai-implement"
# Discovery skips only these three. Stage labels are informational agent progress markers.
AI_STATUS_LABELS = frozenset({AI_IN_PROGRESS, AI_NEED_ATTENTION, AI_COMPLETE})
AI_STAGE_LABELS = frozenset({AI_SPECIFY, AI_PLAN, AI_IMPLEMENT})

# Providers handle_issues_auto.py routes `auto-ai` items between. Defined here rather than in
# that module so handle_limit_pause (below) can reroute an auto-routed item to a different
# provider immediately on a limit hit, without an import cycle.
PROVIDERS = ("claude", "codex", "cursor")
AUTO_LABEL = "auto-ai"

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


def delete_comment(number, comment_id):
    """Deletes one issue/PR comment. `number` is kept only for logging symmetry with the
    module's other per-item helpers - the REST endpoint addresses the comment directly and does
    not nest under the issue number. Callers of this used for best-effort claim-comment cleanup
    must tolerate a 404 (another instance's own cleanup already removed it)."""
    run_gh(["api", "-X", "DELETE", f"repos/{OWNER}/{REPO}/issues/comments/{comment_id}"])


def label_names(item):
    return {item_label["name"] for item_label in item.get("labels", [])}


def get_item_label_names(number):
    """Live label set for one issue/PR (issues API covers both)."""
    labels = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{number}/labels", "--paginate"])
    return {entry["name"] for entry in labels}


def determine_usage_stage_and_spec_dir(logger, number, branch, repo_root):
    """Attributes one candidate's completed CLI run to a Docs/Specs/<dir>/usage.csv row's
    (spec_dir, stage) for the Codex/Cursor `handle_issues.py` wrappers, whose single CLI
    invocation may progress through /specify, /plan, or /implement with no per-stage
    structural marker to segment on - unlike Ralph loop runs, which always know their own
    stage directly (see scripts/automation/{claude,codex}/ralph.py).

    `spec_dir` comes from diffing `branch` against its merge-base with `main` and reusing
    the same Docs/Specs/<dir>/ write-path attribution `scripts/stats/attribution.py` uses
    for interactive-session scans. This wrapper's own `feature/<name>` branch convention
    never matches that module's `ralph/<spec_id>` fallback, so only the file-diff path
    ever resolves a spec_dir here. `stage` comes from the item's live `ai-specify` /
    `ai-plan` / `ai-implement` label (see AI_STAGE_LABELS above) - whichever the agent set
    last and never cleared - falling back to `"implement"` if none survived (e.g. an
    owner or a later run cleared it).

    Returns `(None, None)` when the diff touches no `Docs/Specs/<dir>/` path - most
    `codex`/`cursor`-labeled issues are not spec work, so that is the normal, silent case,
    not an error. A `git`/`gh` failure along the way also returns `(None, None)`, logged as
    a warning, so a usage-stats hiccup can never block or fail the caller's automation run.
    """
    sys.path.insert(0, str(Path(__file__).resolve().parent.parent.parent.parent))
    from scripts.stats.attribution import attribute_segment  # noqa: E402

    try:
        merge_base = run_git(["merge-base", "main", branch])
        changed_paths = run_git(["diff", "--name-only", merge_base, branch]).splitlines()
    except Exception as exc:
        logger.warning("Usage-stats: could not diff '%s' against main for spec attribution: %s",
                        branch, exc)
        return None, None

    spec_dir = attribute_segment(changed_paths, None, repo_root)
    if spec_dir is None:
        return None, None

    try:
        live_labels = get_item_label_names(number)
    except Exception as exc:
        logger.warning("Usage-stats: could not read live labels on #%s: %s - defaulting stage "
                        "to 'implement'.", number, exc)
        live_labels = set()

    stage = next(
        (mapped for label, mapped in
         ((AI_SPECIFY, "spec"), (AI_PLAN, "plan"), (AI_IMPLEMENT, "implement"))
         if label in live_labels),
        "implement",
    )
    return spec_dir, stage


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


def clear_in_progress(logger, number):
    """Best-effort per-item removal of `ai-in-progress`.

    Called from wrapper `finally` blocks and limit paths after a successful claim. Clears only
    the claimed number - never scans other provider-labeled items - so multi-instance / multi-
    candidate runs cannot peer-steal a sibling's live claim. Idempotent: if the label is already
    gone (prior limit clear, 404), logs a warning and continues without raising.
    """
    try:
        remove_label(number, AI_IN_PROGRESS)
        logger.info(f"Cleared {AI_IN_PROGRESS} from #{number}.")
    except Exception as exc:
        logger.warning(f"Could not clear {AI_IN_PROGRESS} from #{number}: {exc} - continuing.")


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
    then always persist `retry_at`, then clear-or-escalate labels for the claimed item only,
    then best-effort note - so a failed `post_comment` cannot undo a successful clear."""
    status, detail = salvage_uncommitted_work(logger)
    save_limit_retry_at(state_file, label, retry_at)
    number = candidate["number"]
    kind = candidate["kind"]
    retry_iso = retry_at.isoformat()

    if status in ("clean", "committed"):
        clear_in_progress(logger, number)
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
        clear_in_progress(logger, number)
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


def _parse_github_timestamp(value):
    """Parses a GitHub API `created_at` string (`...Z`) into an aware-UTC datetime."""
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def claim_candidate(logger, label, marker, candidate, freshness_minutes=10, settle_seconds=5):
    """Cross-instance claim for one candidate, called immediately after discovery and before any
    git/CLI work. Pairs the existing (non-CAS) `add_label(AI_IN_PROGRESS)` with a uniquely-tokened,
    fully invisible claim comment; a short settle delay lets a genuinely racing rival's own claim
    comment land before either side decides; GitHub's monotonic comment id (not the coarser
    per-second `created_at`) then breaks the tie, bounded to comments posted within the last
    `freshness_minutes` so a crash between claiming and cleanup can never permanently deadlock the
    item. Returns True when this attempt won (caller proceeds), False when it lost the race or the
    claim mechanism itself failed (caller must skip the candidate - never a false win).

    Wrapped in one try/except: any exception after `add_label` succeeded best-effort rolls that
    label back before returning False, so a transient failure between steps can't leave
    `ai-in-progress` stuck on an item nobody is working. An exception from `add_label` itself has
    nothing to roll back. Non-exception losses after `add_label` (rival win, empty fresh claims)
    leave the label as-is - empty-fresh leftovers need owner-manual cleanup once reclaim is gone;
    rival wins correctly keep the winner's label."""
    number = candidate["number"]
    kind = candidate["kind"]
    marker_prefix = marker.rsplit(" -->", 1)[0]
    claim_prefix = f"{marker_prefix}:claim:"
    token = uuid.uuid4().hex
    own_body = f"{claim_prefix}{token}: claiming this {kind} for automated processing. -->"

    label_added = False
    try:
        add_label(number, AI_IN_PROGRESS)
        label_added = True

        post_comment(number, own_body)
        time.sleep(settle_seconds)
        comments = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{number}/comments", "--paginate"])

        cutoff = datetime.now(timezone.utc) - timedelta(minutes=freshness_minutes)
        fresh = sorted(
            (c for c in comments
             if c.get("body", "").startswith(claim_prefix)
             and _parse_github_timestamp(c["created_at"]) >= cutoff),
            key=lambda c: c["id"],
        )

        if not fresh or not fresh[0]["body"].startswith(f"{claim_prefix}{token}"):
            logger.info(f"Lost claim race for '{label}' {kind} #{number} - skipping this candidate.")
            return False

        for fresh_comment in fresh:
            try:
                delete_comment(number, fresh_comment["id"])
            except Exception as exc:
                logger.warning(f"Could not delete claim comment {fresh_comment['id']} on "
                                f"'{label}' {kind} #{number} during cleanup: {exc} - continuing.")

        logger.info(f"Won cross-instance claim for '{label}' {kind} #{number}.")
        return True
    except Exception as exc:
        logger.warning(f"Claim attempt failed for '{label}' {kind} #{number}: {exc} - "
                        "treating as a loss.")
        if label_added:
            try:
                remove_label(number, AI_IN_PROGRESS)
            except Exception as rollback_exc:
                logger.warning(f"Rollback of {AI_IN_PROGRESS} failed for '{label}' {kind} "
                                f"#{number}: {rollback_exc}.")
        return False
