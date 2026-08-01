# Implementation Plan: Prevent Double Automation Work

## Spec

As the repository owner, at most one automation instance must ever process a given issue/PR at a time, so two nearly-simultaneous cron ticks (possibly on separate machines) cannot both work the same item and produce competing branches/PRs. Today `ai-in-progress` is applied too late — as the first action *inside* the `claude -p` CLI run (`.claude/commands/handle-issue.md` step 1) — well after the wrapper's own `find_candidates` discovery already built its candidate list. A plain "add label" GitHub call is not compare-and-swap, so simply moving it earlier narrows but does not close the race. This reproduced on issue #104: a stale-reclaim at 18:10:05 UTC let two separate runs both pick the item up, producing PR #105 (18:14:03 UTC) and PR #106 (18:18:47 UTC) for the same feature.

Acceptance criteria (from `spec.md`):
- Two instances contest the same candidate at roughly the same time → exactly one wins → only the winner executes the prompt.
- The loser does no branch/commit/push/PR/summary-comment work for that item, leaves the winner's `ai-in-progress` label and coordination markers untouched, and simply moves on.
- An uncontested claim behaves exactly as today (no visible behavior change).
- The claim runs before any expensive/irreversible work — a lost race costs only a few cheap GitHub API calls.
- A reclaimed item (`reclaim_stale_in_progress` freed it after a crash) gets the same claim protection as a never-claimed candidate.
- Two instances racing on *different* candidates in the same batch don't interfere with each other.
- If the claim mechanism itself fails/errors, that instance treats it as a loss — never a false win.

Tech notes: the fix belongs in shared `scripts/automation/common/issue_handler.py` (not per-provider wrappers); the claim must move to immediately after a candidate is selected in the wrapper's loop, before the CLI is spawned; GitHub comments have strict server-assigned chronological order, usable as a tie-breaker substrate; must stay compatible with `reclaim_stale_in_progress`/`MAX_AUTO_RECLAIMS` and the limit-pause path (`handle_limit_pause`, `release_in_progress_silently`); keep `acquire_lock` (`flock`) as-is — it only serializes one process's own loop; no new external infra (no DB, no distributed lock service).

## Goal

Add a provider-agnostic `attempt_claim` step to `issue_handler.py` that every provider wrapper calls immediately before processing each candidate (before `checkout_clean`, before the CLI is spawned), so a lost race is detected and abandoned while it is still cheap — closing the exact race that produced PRs #105/#106.

## Approach

Pair the existing (non-CAS) `add_label(AI_IN_PROGRESS)` with a uniquely-tokened claim comment and GitHub's strict chronological comment ordering as the tie-breaker, matching this repo's existing "labels + comments are the whole state machine" design (same substrate `count_reclaims_since_owner_comment`/`reclaim_stale_in_progress` already use):

1. Add `ai-in-progress` (idempotent — fine if a rival instance already added it too).
2. Post a comment whose body starts with a token unique to this attempt (`f"{marker_prefix}:claim:{token}: ..."`) and stays entirely inside a single HTML comment (`... -->` at the end, nothing rendered visibly) where `token` is a fresh `uuid.uuid4().hex[:12]` and `marker_prefix` is the provider's existing `marker.rsplit(" -->", 1)[0]` (e.g. `<!-- claude-automation`).
3. Re-fetch the item's full comment list (`gh api .../comments --paginate`, already documented elsewhere in this file as returning comments in strict chronological/server-insertion order).
4. Filter to comments whose body starts with the *un-tokened* claim prefix `f"{marker_prefix}:claim:"` — i.e. any instance's claim attempt for this item, ever. Find this attempt's own comment (exact token match).
5. Walk the filtered list **in the order returned by the API** (already chronological) up to (not including) this attempt's own comment. For each earlier same-kind comment, first check whether a `{marker_prefix}:reclaim -->` comment (or a terminal outcome comment) for this item was posted between that earlier claim comment and this attempt's own comment — if so, that earlier claimant's cycle already ended (a crash-and-reclaim, not a live rival) and it is ignored regardless of elapsed time. Otherwise, parse its `created_at` and compare to this attempt's own `created_at`: if the two are within `CLAIM_RACE_WINDOW_SECONDS` of each other, treat it as a genuine rival in *this* race and this attempt has lost. An earlier claim comment further away than the window, with no intervening reclaim boundary, is an unrelated prior cycle (item fully processed once and later reopened by the owner for reprocessing) and is ignored.
6. If nothing within the window precedes this attempt's own comment, it has won. If own comment is missing after the re-fetch, or any GitHub call in this sequence raises, the attempt is treated as a loss (never a false win).

Why the window instead of "always first ever": an item can be legitimately reclaimed/resumed and reprocessed later, and would otherwise carry an old claim comment forever that makes every future claim un-winnable. `CLAIM_RACE_WINDOW_SECONDS = 3600` (1 hour) comfortably covers the described race gap (CLI startup latency plus the full processing time of every earlier candidate in a batch) while a genuine owner-driven resume cycle — which requires the owner to notice, reply, and remove a status label — realistically takes far longer, so it never falls inside the window.

The window alone is not enough to distinguish a live rival from this item's own dead history, though: `reclaim_stale_in_progress` can free a crashed item and have it re-claimed again in the very same run, minutes (not hours) after the original crashed claim comment — well inside the window. To avoid the reclaimed attempt spuriously losing to its own prior claim comment, an earlier claim comment only counts as a rival if no `{marker_prefix}:reclaim -->` comment for this item was posted between it and this attempt's own comment; a reclaim marker in between means that earlier claimant's cycle already ended, regardless of elapsed time. This reuses the same comment-boundary idiom `count_reclaims_since_owner_comment` already relies on.

Why list order, not a `created_at` comparison, decides *who* wins: `created_at` has only 1-second resolution, too coarse to break a true tie between near-simultaneous writers; the API's return order is the server's actual insertion order (already relied on by `count_reclaims_since_owner_comment`'s "comments in chronological order" comment), so it's the precise tie-breaker. `created_at` is only used for the coarse in/out-of-window decision, where 1-second resolution doesn't matter.

The loser never calls `remove_label` — its own earlier `add_label` call is harmless (idempotent), and it does nothing further, satisfying "leaves the winner's label and markers untouched." If both instances' claim mechanism fails outright (e.g. a `gh` outage) after at least one `add_label` succeeded, the item is left with `ai-in-progress` and no further progress — self-healing, since the next run's `reclaim_stale_in_progress` picks it up as a stale in-progress item exactly like a crash.

## Steps

1. **`scripts/automation/common/issue_handler.py`** — add:
   - `import uuid` (new top-level import).
   - `CLAIM_MARKER_INFIX = ":claim"` and `CLAIM_RACE_WINDOW_SECONDS = 3600` constants near `MAX_AUTO_RECLAIMS`.
   - `attempt_claim(logger, label, marker, candidate)` implementing the protocol above. Signature/usage mirrors `reclaim_stale_in_progress(logger, label, marker)`. Returns `True` (claim won, caller proceeds) or `False` (claim lost or claim mechanism failed, caller skips this candidate). Internals:
     - `number = candidate["number"]`; `kind = candidate["kind"]`.
     - `marker_prefix = marker.rsplit(" -->", 1)[0]` (same derivation `reclaim_stale_in_progress` already uses).
     - `token = uuid.uuid4().hex[:12]`; `claim_prefix = f"{marker_prefix}{CLAIM_MARKER_INFIX}:"`; own body `f"{claim_prefix}{token}: claiming this {kind} for automated processing. -->"` — the entire body stays inside a single HTML comment (unlike the reclaim/outcome comments, which are meant for the owner to read) so an uncontested claim renders nothing visible on the thread, matching the spec's "no behavior change is visible in the non-contention case" criterion.
     - Wrap the *entire* remaining protocol in one `try/except Exception as exc:` (matching `salvage_uncommitted_work`'s whole-function defensive pattern elsewhere in this file), so any failure anywhere in the sequence — not just the initial GitHub calls — logs a warning and `return False`:
       - `add_label(number, AI_IN_PROGRESS)`; `post_comment(number, own_body)`; `comments = run_gh_json(["api", f"repos/{OWNER}/{REPO}/issues/{number}/comments", "--paginate"])`.
       - Find `own = next((c for c in comments if c["body"].startswith(f"{claim_prefix}{token}")), None)`; if `None`, log a warning and `return False`.
       - Parse `own`'s `created_at` with a small local helper, e.g. `_parse_github_timestamp(value)` → `datetime.fromisoformat(value.replace("Z", "+00:00"))` (aware UTC, same convention as the rest of the file).
       - `rivals = [c for c in comments if c["body"].startswith(claim_prefix) and not c["body"].startswith(f"{claim_prefix}{token}")]`, walked in the (already chronological) list order up to `own`'s position; for each `rival` appearing before `own` in that order, first check whether a `{marker_prefix}:reclaim -->` comment for this item falls between `rival` and `own` in the list — if so, skip it (dead prior cycle, not a live rival); otherwise if `abs((own_time - rival_time).total_seconds()) <= CLAIM_RACE_WINDOW_SECONDS`, log a warning and `return False`.
       - Otherwise log an info line and `return True`.
   - Update the module's top-of-file docstring with a short new paragraph describing the cross-instance claim protocol (placed near the existing "Stale-run reclaim" paragraph), so the file's own design writeup stays authoritative.

2. **Wire into each provider wrapper's candidate loop** — in `scripts/automation/claude/handle_issues.py`, `scripts/automation/codex/handle_issues.py`, `scripts/automation/cursor/handle_issues.py`: import `attempt_claim` from `common.issue_handler`, and call it as the very first action for each candidate in the `for candidate in candidates:` loop, before `checkout_clean`:
   ```python
   for candidate in candidates:
       if not attempt_claim(logger, LABEL, MARKER, candidate):
           logger.info(f"Lost claim race for {candidate['kind']} #{candidate['number']} - skipping.")
           continue
       branch = candidate_branch(candidate)
       checkout_clean(logger, branch)
       ...
   ```
   This must run before any git/checkout work per the acceptance criteria ("no branch/commit/push, no PR creation, and does not begin executing the item's prompt" for a lost race). `cursor/handle_issues.py`'s loop currently has no per-candidate `continue`/`return` branching for a skip case — add one that mirrors the existing limit-hit `return` but continues instead of stopping the whole run.

3. **Make the CLI-side "Claim" step a no-op/verification note, not a second claim** — `.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, `.cursor/commands/cursor-issue.md` each have their candidate-lifecycle step 1 currently reading `"Claim — add ai-in-progress as the very first action on the item."` (or `.cursor`'s equivalent numbered step 1, `"Add ai-in-progress first."`). Replace each with wording to this effect (keep the surrounding step numbering 1–7 unchanged so all "step 6"/"step 7" cross-references elsewhere stay correct):
   > **1. Claim already won** — the wrapper's cross-instance claim (`attempt_claim` in `issue_handler.py`) already applied `ai-in-progress` and confirmed this run won the race before invoking this CLI. Do not add or remove `ai-in-progress` yourself here. If it is unexpectedly missing when you check the item in step 2, that indicates a wrapper bug, not something to fix by adding the label yourself — stop and finish with `ai-need-attention` instead.

4. **Add unit tests** — `scripts/automation/common/test_issue_handler.py`, new `AttemptClaimTests` class (see Tests section below for the exact cases), following the existing `patch(...)`-per-collaborator style used by `ReclaimStaleInProgressTests`/`HandleLimitPauseTests`.

5. **Update automation documentation** — `.claude/skills/github-issue-automation/SKILL.md`'s "Concurrency" section currently only describes the local `flock` and explicitly says it "does nothing for two distinct automation instances." Add a short new subsection (or extend "Concurrency") describing the cross-instance claim protocol: label + uniquely-tokened claim comment + chronological-order tie-break within `CLAIM_RACE_WINDOW_SECONDS`, called per-candidate before `checkout_clean`. Cross-reference issue #104/PRs #105/#106 as the motivating case, matching how other sections in that file cite specifics.

6. **Run the test suite** — `python -m unittest scripts.automation.common.test_issue_handler` (or the project's existing test-running convention for this module) and confirm all tests pass, including the new `AttemptClaimTests`.

## Constitution Check

No conflicts found — plan aligns with all principles. This plan touches only Python automation tooling (`scripts/automation/`) and its accompanying command/skill docs; it makes no Unity, ECS, VContainer, UI Toolkit, or C# changes, so the Rendering/Game Logic/Dependency Injection/UI/Assembly Structure/C# Code Style principles do not apply. Planning Discipline's "Plan before implement" is satisfied by this plan existing and gating implementation. Specification Discipline is satisfied — `spec.md` already exists in this same folder and this plan implements its acceptance criteria without deviation. File Organisation is satisfied — this plan lives at `Docs/Specs/26_07_31_23_prevent-double-automation/plan.md`, matching the required `Docs/Specs/<YY_MM_DD_HH>_<name>/` convention alongside the existing `spec.md`.

## Tests

All in `scripts/automation/common/test_issue_handler.py`, new `AttemptClaimTests` class, mocking `add_label`, `post_comment`, and `run_gh_json` the same way `ReclaimStaleInProgressTests` does, plus patching `uuid.uuid4` (or the module's `uuid` import) for deterministic tokens:

- **Uncontested claim wins** — `run_gh_json` (the re-fetch) returns only this attempt's own claim comment. `attempt_claim` returns `True`; `add_label` was called with `AI_IN_PROGRESS`; `post_comment` was called once with a body starting with the expected `{marker_prefix}:claim:{token} -->` prefix.
- **Contested race has exactly one winner** — re-fetch returns two claim comments in list order (an earlier rival token, then this attempt's own token), both `created_at` within `CLAIM_RACE_WINDOW_SECONDS` of each other. `attempt_claim` for the later one returns `False`. A second test with the tokens/order swapped (own comment first in the list) returns `True` for that side — proving the winner is whichever comment the API actually returned first, not attempt order in the test.
- **Old unrelated claim comment outside the window is ignored** — re-fetch returns an old claim comment (different token) with `created_at` far outside `CLAIM_RACE_WINDOW_SECONDS` before this attempt's own comment, plus this attempt's own comment. `attempt_claim` returns `True` (a stale/resumed item's ancient claim comment must not permanently block reclaiming).
- **A reclaim within the window is not mistaken for a rival** — re-fetch returns an earlier claim comment (different token) only ~20 minutes before this attempt's own comment (well inside `CLAIM_RACE_WINDOW_SECONDS`), plus a `{marker_prefix}:reclaim -->` comment posted between them, plus this attempt's own comment. `attempt_claim` returns `True` — reproduces the crash → reclaim → immediate re-claim sequence in a single run and proves it never loses to its own dead history.
- **Own comment missing after re-fetch is a loss** — re-fetch returns comments with no match for this attempt's token (e.g. eventual-consistency read lag). `attempt_claim` returns `False` and logs a warning.
- **`add_label` failure is a loss** — `add_label` raises; `attempt_claim` returns `False` without calling `post_comment`.
- **`post_comment` failure is a loss** — `add_label` succeeds, `post_comment` raises; `attempt_claim` returns `False`.
- **Re-fetch (`run_gh_json`) failure is a loss** — `add_label`/`post_comment` succeed, the re-fetch raises; `attempt_claim` returns `False`.
- **Reclaim compatibility** — a candidate produced by `find_candidates` after `reclaim_stale_in_progress` removed a stale `ai-in-progress` label goes through `attempt_claim` identically to a never-claimed candidate (i.e. no test-visible special-casing exists in `attempt_claim` for reclaimed items — verified by the fact none of the above tests need to distinguish candidate provenance).
- **Different candidates don't interfere** — two `attempt_claim` calls for different `number`s each only read/write comments scoped to their own `number` (verified via the exact `number` argument passed into the mocked `run_gh_json`/`post_comment`/`add_label` calls in one of the above tests, or a small dedicated test asserting the GitHub API path includes the correct issue number).

## Section 1 — Agent Steps

- [ ] **Add `attempt_claim` and its constants to `issue_handler.py`** — `CLAIM_MARKER_INFIX`, `CLAIM_RACE_WINDOW_SECONDS`, the `uuid` import, `attempt_claim(logger, label, marker, candidate)` per the Steps section, plus the module-docstring paragraph describing the protocol.
- [ ] **Wire `attempt_claim` into the claude wrapper** — `scripts/automation/claude/handle_issues.py`'s candidate loop calls it before `checkout_clean` and `continue`s on a lost claim.
- [ ] **Wire `attempt_claim` into the codex wrapper** — same change in `scripts/automation/codex/handle_issues.py`.
- [ ] **Wire `attempt_claim` into the cursor wrapper** — same change in `scripts/automation/cursor/handle_issues.py`, adding the missing per-candidate skip/continue branch its loop currently lacks.
- [ ] **Rewrite the CLI-side "Claim" step in all three lifecycle docs** — `.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, `.cursor/commands/cursor-issue.md` step 1 becomes the verification-only note; step numbering stays 1–7 so other cross-references remain valid.
- [ ] **Add `AttemptClaimTests` to `test_issue_handler.py`** — all cases listed in the Tests section above.
- [ ] **Update `github-issue-automation` SKILL.md's Concurrency section** — document the new cross-instance claim protocol alongside the existing `flock` explanation, citing issue #104/PRs #105/#106 as the motivating case.
- [ ] **Run the automation test suite and confirm green** — including the new `AttemptClaimTests`, with no regressions in the existing reclaim/limit-pause/discovery tests.

## Section 2 — User Steps

None — this is a pure automation/tooling change with no Unity Editor involvement.

Use the implement skill to start working on the plan or request changes.
