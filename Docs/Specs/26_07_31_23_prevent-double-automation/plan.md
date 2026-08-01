# Plan: Prevent Double Automation Work

## Note: this branch already raced

While this plan was being written, a second, independently-scheduled automation instance
concurrently processed this same issue and pushed its own spec/plan work to this branch
(`1f4aac1`/`ba8b481`) — a live instance of the exact bug this feature fixes. Both plans converged on
the same core design (label + uniquely-tokened claim comment + GitHub's strict comment ordering as the
tie-break substrate) but differ on the concurrency-safety details. This document reconciles the two:
it keeps the freshness-window-based design below (which underwent an independent adversarial review
pass that caught and fixed a real evidence-erasure race — see Approach), and adopts one genuinely
better detail from the other draft (fully invisible claim-comment bodies). The alternative
reclaim/terminal-comment-boundary design is noted at the end of Approach along with why it was not
carried forward as-is.

## Spec

Full spec: `Docs/Specs/26_07_31_23_prevent-double-automation/spec.md`.

At most one automation instance may ever process a given issue/PR at a time. Today `ai-in-progress`
is applied as the *first action inside* the spawned CLI run (`.claude/commands/handle-issue.md` step
1, and the Codex/Cursor equivalents) — well after the wrapper's own `find_candidates` already built
its candidate list. A second, independently-scheduled instance whose own discovery falls inside that
gap picks up the same item and starts a competing run. This reproduced on issue #104: a stale-label
reclaim at 18:10:05 UTC was picked up by two separate runs, producing duplicate PRs #105/#106 four
minutes forty-four seconds apart. Because `add_label` is not compare-and-swap, simply moving it
earlier narrows the race window but cannot by itself guarantee a single winner.

Acceptance criteria (condensed — see spec.md for the full `Precondition => Action => Outcome` table):

- Two instances racing the same candidate: exactly one wins the claim and proceeds; the other does
  not branch, commit, push, open a PR, or comment for that item, and backs off cheaply (no label
  removal, no cleanup) leaving the winner's work undisturbed.
- Uncontested claim: behavior is unchanged from today (read prompt → execute → commit/push → PR →
  summary comment → terminal label).
- The claim happens before any expensive or irreversible work — no checkout, no CLI invocation, on a
  lost race.
- A reclaimed stale item (crash recovery) gets the same claim protection as a never-claimed item.
- Two instances racing *different* candidates in the same batch each independently win their own item
  — no cross-item interference.
- A claim-mechanism failure (e.g. a `gh` API error mid-claim) is treated as a loss, never a win.

Out of scope (per spec): implementing beyond this plan's surface, redesigning the reclaim/retry
counter beyond what atomic claiming requires, external locking infrastructure (DB/Redis/queue),
cleaning up the existing duplicate PRs #105/#106, and changes to limit-pause/`auto-ai`
routing/fairness beyond what the new claim step requires touching.

## Goal

Move the `ai-in-progress` claim out of the CLI run and into each provider wrapper's own loop, executed
immediately after a candidate is selected and before `checkout_clean` or the CLI is spawned, with a
GitHub-comment-creation-order tie-break so a genuinely simultaneous race between two instances still
resolves to exactly one winner.

## Approach

Add one new shared function to `scripts/automation/common/issue_handler.py`:

```
claim_candidate(logger, label, marker, candidate, freshness_minutes=10, settle_seconds=5) -> bool
```

Each provider wrapper's `main()` loop calls it once per candidate, positioned before `checkout_clean`
(checkout does `git fetch`/`git reset` network calls, so the claim must precede even that) and
obviously before the CLI/agent is spawned. A `False` return means "lost or errored" — the wrapper logs
and `continue`s to the next candidate: no checkout, no CLI invocation, no branch/commit/PR/comment for
that item.

Inside `claim_candidate`, wrapped in one `try/except` (any exception → best-effort rollback, then
`return False`, satisfying the "claim-mechanism failure is treated as a loss" acceptance criterion):

1. `add_label(number, AI_IN_PROGRESS)` — idempotent; safe if both instances call it. If any later step
   in this `try` raises, the `except` must best-effort `remove_label(number, AI_IN_PROGRESS)` (itself
   wrapped so a failed rollback can't raise past the outer handler) before returning `False` —
   otherwise a transient failure between steps (e.g. `post_comment` hiccups right after a successful
   `add_label`) leaves `ai-in-progress` stuck on an item nobody is working, which the next
   `reclaim_stale_in_progress` pass would then treat as a genuine crash and wrongly burn one of the
   `MAX_AUTO_RECLAIMS` (2) retry slots.
2. Post a uniquely-tokened claim comment whose **entire body stays inside a single HTML comment** —
   `f"{claim_prefix}{token}: claiming this {kind} for automated processing. -->"`, where
   `claim_prefix = f"{marker_prefix}:claim:"` and `token` is a fresh random value (`uuid.uuid4().hex`)
   generated per attempt (not the item's static automation marker, so concurrent claims from different
   instances are distinguishable). Putting the closing `-->` at the very end — not right after the
   token, with descriptive text trailing outside it — means an uncontested *and* a contested claim both
   render nothing visible on the thread, satisfying "no behavior change is visible in the non-contention
   case" even in the narrow window before cleanup runs, not just after. This does not collide with the
   existing reclaim marker: `reclaim_marker = f"{marker_prefix}:reclaim -->"` starts with
   `{marker_prefix}:reclaim`, while every claim comment starts with `{marker_prefix}:claim:` — the
   literal text right after `marker_prefix` differs (`:reclaim` vs `:claim:`), so `startswith` checks
   against either prefix never cross-match.
3. **Settle before deciding**: sleep `settle_seconds` (default 5) after posting, before re-listing.
   Without this, a fast instance can post its comment, re-list, see only itself, declare itself the
   winner, and delete its own comment — all before a genuinely-racing-but-slightly-slower instance has
   even posted its own comment. That second instance then lists an empty/self-only set and *also*
   declares itself the winner, silently defeating the tie-break for the exact near-simultaneous-cron
   scenario (issue #104) this feature exists to close. The settle delay gives both sides' posts time to
   land before either one is allowed to decide or clean up.
4. Re-list the item's comments (`gh api .../comments --paginate`, the same raw REST call
   `count_reclaims_since_owner_comment` already uses — the REST comment object already carries `id`
   and `created_at`, no special field selection needed). Filter to comments whose body starts with
   `claim_prefix`.
5. **Bound to "fresh" comments only**: keep claim comments whose `created_at` is within the last
   `freshness_minutes` (default 10) of "now" (UTC). This bound is required, not optional: if the
   winning instance ever crashes after posting its claim comment but before reaching the cleanup step
   below, that comment would otherwise sort first *forever*, permanently deadlocking every future claim
   attempt on that item. 10 minutes is chosen as headroom above the ~5-minute gap the spec's own #104
   timeline reports between two racing instances (so legitimately-close claims from a real race are
   never mistaken for stale leftovers), while staying clearly *shorter* than the documented cron cadence
   (all four wrapper `.sh`/`.ps1` files' example schedules poll every 15 minutes) — a value at or above
   15 minutes would let a crash-orphaned comment survive into and confuse the very next poll tick.
6. Sort the surviving fresh comments by comment `id` ascending — a monotonic server-assigned integer,
   so this is a safe int compare with no `created_at` string/timezone parsing involved in the actual
   tie-break (the freshness bound above is the only place `created_at` parsing happens).
7. The earliest fresh claim comment's token decides the winner:
   - If it matches this attempt's own `token`, this instance won. Best-effort delete *every* fresh
     claim comment on the item (its own and any competitors') via the new `delete_comment` helper, so
     the thread's comment count doesn't grow unboundedly across repeated processing cycles. Log,
     `return True`.
   - If it doesn't match, this instance lost. Do nothing else — no cleanup, no label removal (the
     "loser backs off cheaply" acceptance criterion: it must leave the winner's `ai-in-progress` and
     markers untouched). Log, `return False`.
   - If no fresh claim comment is found at all (shouldn't happen — this attempt's own post should
     always be there), treat as a loss defensively: log, `return False`.

Add one new shared helper:

```
delete_comment(number, comment_id)
```

`DELETE /repos/{OWNER}/{REPO}/issues/comments/{comment_id}` — unlike `post_comment`/`add_label`/
`remove_label`, this REST endpoint addresses the comment directly and does not nest under the issue
number; `number` is kept as a parameter only for logging symmetry with the module's other per-item
helpers, not because the URL needs it. Deletes are best-effort: callers must tolerate a 404 (the
other instance's own cleanup, or a previous partial cleanup, already removed it), so `claim_candidate`
catches and logs per-delete failures rather than letting one failed delete abort the rest of the
cleanup loop.

`count_reclaims_since_owner_comment` (unchanged) already tolerates claim comments correctly: its loop
only resets the counter on a comment that is contributor-authored *and* does not start with
`marker_prefix`. Claim comments start with `{marker_prefix}:claim:`, so they always satisfy
`body.startswith(marker_prefix)` and are exempted from resetting the counter — including in the
failure mode where a claim comment is left behind by an interrupted cleanup. No code change needed
here; this is a verification step in Steps below, not an edit.

`.claude/commands/handle-issue.md` documents no manual-invocation path (only "Invoked by
`scripts/automation/claude/handle_issues.py`"), so its CLI-side "Claim" step is removed outright and
the remaining lifecycle steps renumbered — the wrapper now owns claiming unconditionally before the
CLI ever runs, so there is nothing left for that step to conditionally acknowledge.

`.codex/skills/codex-issue/SKILL.md` and `.cursor/commands/cursor-issue.md`, by contrast, both
explicitly document a manual-invocation path that never goes through a wrapper's `claim_candidate`
("Use when processing a `codex`-labeled item manually or from `scripts/automation/codex/
handle_issues.py`"; "It may also be run manually in Cursor via `/cursor-issue`"). Deleting their step 1
outright would (a) leave a manual run without `ai-in-progress` at all, so it could race an automated
wrapper run undetected, and (b) make their unchanged final hand-off step's unconditional
`remove_label(number, AI_IN_PROGRESS)` raise on a label that was never added. Their step 1 is reworded,
not deleted, to: "the wrapper already added `ai-in-progress` before invoking you; if running this
manually outside the wrapper, add it yourself now as the first action" — staying step 1 so hand-off
stays consistent either way.

**Alternative design considered and not carried forward as-is:** the other concurrent draft on this
branch used no settle delay and no cleanup, instead relying on comments never being deleted plus a
1-hour `created_at` window gated by "no reclaim-marker comment falls between the two claims." That
avoids needing a settle delay (nothing is ever erased, so a late-arriving rival always finds the
earlier claim intact) but has an internal gap: its own Approach section says a *terminal outcome*
comment should also break the boundary (not just a reclaim marker), while its Steps section only
implements the reclaim-marker check. Taken literally, that means a common, non-crash workflow — an
item marked `ai-need-attention`, promptly answered and reopened by the owner minutes later — would see
its own prior claim comment (never deleted, well inside the 1-hour window, no reclaim marker in
between) as a live rival and lose to its own dead history. The freshness-window design above avoids
this whole class of bug by expiring *every* stale claim comment the same way after a fixed short
duration, regardless of how the previous cycle ended, at the cost of needing the settle delay (fixed
above) and a cleanup step.

## Steps

### Section 1 — Agent Steps

- [ ] **Add `claim_candidate` and `delete_comment` to `issue_handler.py`** — implement both functions
      as described in Approach (imports needed: `uuid` for token generation and `time.sleep` for the
      settle delay; `datetime`/`timezone` are already imported (`from datetime import datetime,
      timezone`), but `timedelta` is not — add it to that import line). Use
      `marker.rsplit(" -->", 1)[0]` for `marker_prefix`, matching `reclaim_stale_in_progress`'s existing
      convention exactly.
- [ ] **Unit-test `claim_candidate` and `delete_comment` in `test_issue_handler.py`** — add a new test
      class following the file's existing `item(...)`/`comment(...)` helper + `unittest.mock.patch` on
      `scripts.automation.common.issue_handler.run_gh_json`/`run_gh` conventions (see Tests below for
      the required cases). Land this before wiring the wrappers so the core mechanism is verified in
      isolation first.
- [ ] **Wire `claude/handle_issues.py`'s `main()` loop** — import `claim_candidate` from
      `common.issue_handler`; in the `for candidate in candidates:` loop (currently starting with
      `branch = candidate_branch(candidate)` then `checkout_clean(logger, branch)`), call
      `claim_candidate(logger, LABEL, MARKER, candidate)` first and `continue` to the next candidate on
      `False`, logging that the claim was lost, before any `candidate_branch`/`checkout_clean` call.
- [ ] **Wire `codex/handle_issues.py`'s `main()` loop** — same change, same insertion point (before
      `branch = candidate_branch(candidate)` / `checkout_clean(logger, branch)` in its `for candidate in
      candidates:` loop), using that module's own `LABEL`/`MARKER`.
- [ ] **Wire `cursor/handle_issues.py`'s `main()` loop** — same change; note this loop's current shape
      is `checkout_clean(logger, candidate_branch(candidate))` called inline as the first statement (no
      separate `branch = ...` line, and the file uses tab indentation, unlike the other two wrappers) —
      insert the `claim_candidate` call and `continue`-on-loss before that inline `checkout_clean` call,
      matching the file's existing tab indentation.
- [ ] **Remove the CLI-side "Claim" step and renumber** in `.claude/commands/handle-issue.md` (currently
      numbered step 1, "**Claim** — add `ai-in-progress` as the very first action on the item." under
      "## Candidate lifecycle", steps 1–7; this file documents no manual-invocation path) — delete
      step 1 and renumber the remaining six steps 1–6.
- [ ] **Reword (not delete) the CLI-side "Claim" step** in `.codex/skills/codex-issue/SKILL.md` (its
      frontmatter documents a manual-invocation path — "Use when processing a `codex`-labeled item
      manually or from `scripts/automation/codex/handle_issues.py`") — keep it as step 1 but reword to
      note the wrapper already added `ai-in-progress` before invoking the CLI, and instruct a manual
      run to add it itself first, so the unchanged final `remove_label(..., AI_IN_PROGRESS)` hand-off
      step never targets a label that was never added.
- [ ] **Reword (not delete) the CLI-side "Claim" step** in `.cursor/commands/cursor-issue.md` (it
      documents "It may also be run manually in Cursor via `/cursor-issue`") — same reword as the Codex
      doc, preserving this file's terser phrasing style, keeping it as step 1.
- [ ] **Re-verify `count_reclaims_since_owner_comment` needs no code change** — confirm (per Approach)
      that claim comments' `{marker_prefix}:claim:` prefix satisfies the existing
      `body.startswith(marker_prefix)` exemption so a leftover claim comment from a failed cleanup can
      never falsely reset the reclaim counter; add a regression test for this case in
      `test_issue_handler.py`'s existing `CountReclaimsTests` class rather than changing the function.
- [ ] **Update `github-issue-automation` SKILL.md's Concurrency section** — it currently describes only
      the local `flock` and explicitly says it "does nothing for two distinct automation instances."
      Add a short subsection describing the new cross-instance claim protocol (label + uniquely-tokened,
      fully-invisible claim comment + settle delay + freshness-bounded comment-id tie-break, called
      per-candidate before `checkout_clean`), citing issue #104/PRs #105/#106 as the motivating case —
      and this branch's own concurrent-plan race as a second, even more direct example.
- [ ] **Check whether `test_handle_issues_auto.py` or per-provider tests need a new "skip candidate on
      lost claim" case** — `test_handle_issues_auto.py` covers auto-routing (`route_candidates`), not
      the provider wrappers' own `main()` loops, and there are currently no dedicated
      `test_handle_issues_claude.py`/`codex`/`cursor` files — the three wrappers' `main()` functions are
      not unit-tested today (only their pure helpers like `detect_session_limit` are, via imports into
      `test_issue_handler.py`). Since `claim_candidate` itself is fully covered at the
      `issue_handler.py` level, and the wrapper-loop change is a thin "call it, `continue` on `False`"
      edit with no new branching logic of its own, no new test file is needed to keep the change
      covered at the same rigor as the rest of each `main()` loop — confirm this reasoning still holds
      once the wiring is written (i.e. no non-trivial new logic snuck into the wrapper loop itself) and
      only add a test if it did.
- [ ] **Run the automation test suite and confirm green** — including all new `claim_candidate`/
      `delete_comment` tests, with no regressions in the existing reclaim/limit-pause/discovery tests.

### Section 2 — User Steps

None — this is a pure Python/documentation change with no Unity Editor component.

## Tests

All new coverage lands in `scripts/automation/common/test_issue_handler.py`, in a new test class
(e.g. `ClaimCandidateTests`) following the file's existing conventions: `item(...)`/`comment(...)`
helper factories, `unittest.mock.patch` on `scripts.automation.common.issue_handler.run_gh_json` /
`run_gh` (never real network calls).

- Uncontested claim: only this attempt's own claim comment exists → `claim_candidate` returns `True`,
  `add_label(number, AI_IN_PROGRESS)` was called, and cleanup deleted the one claim comment. Mock
  `time.sleep` (the settle delay) so the test doesn't actually wait.
- Contested claim, this instance wins: two fresh claim comments exist with different tokens/ids; the
  one with the lower `id` carries this attempt's own token → returns `True` and both comments get
  deleted.
- Contested claim, this instance loses: the lower-`id` fresh comment carries a *different* token →
  returns `False`, and no `delete_comment`/label-removal call happens (loser touches nothing).
- Three-or-more-way contested claim: three-plus fresh claim comments with different tokens/ids,
  returned out of `id` order by the mocked `run_gh_json` (to also exercise the explicit sort, not just
  a pre-sorted mock); the lowest-`id` one carries this attempt's own token → returns `True` and *all*
  comments get deleted. A two-comment case alone wouldn't catch an adjacent-pair-only comparison bug
  instead of a true min-by-id.
- Stale claim comments are ignored: a claim-prefixed comment with `created_at` older than
  `freshness_minutes` is excluded from the ordering, so it cannot become a permanent false winner (the
  crash-before-cleanup deadlock scenario from Approach).
- A quick, non-crash need-attention → resolved → reprocessed cycle does not self-collide: a claim
  comment from a *prior, cleanly-completed* cycle should not exist at all (the winner always cleans up
  before the CLI runs), but assert this explicitly — no stray claim comment from a normal completion is
  ever left for a later cycle to trip over — as the regression case for the failure mode identified in
  the "Alternative design considered" note.
- Claim-mechanism exception after a successful `add_label` rolls back: `post_comment` (or the re-list
  call) raising → `claim_candidate` returns `False` *and* `remove_label(number, AI_IN_PROGRESS)` was
  called, so a transient failure doesn't leave `ai-in-progress` stuck on an unworked item.
- Claim-mechanism exception on the very first call is still a clean loss: `add_label` itself raising →
  `claim_candidate` returns `False` without calling `remove_label` (nothing to roll back) and without
  propagating.
- Winner's cleanup tolerates a 404: `delete_comment` raising for one of the fresh comments during
  cleanup does not stop the rest of cleanup and does not make `claim_candidate` return `False` after
  it has already determined it won.
- Claim comment body renders nothing visible: assert the posted body's `-->` closer is the very last
  characters of the string (no trailing markdown text after it), so a claim is invisible on the thread
  even mid-race, not just after cleanup.
- `count_reclaims_since_owner_comment` regression: a claim-prefixed comment (`{marker_prefix}:claim:
  ...`) present in the comment list does not reset the reclaim counter, verifying the exemption noted
  in Approach.

Also confirm (per the relevant Agent Step) whether `test_handle_issues_auto.py` or a new per-provider
test file needs a case for the wrapper's "skip candidate on lost claim" branch — expected answer is no,
per the reasoning in that step, but this must be re-checked against the actual diff once written rather
than assumed.

## Constitution Check

No conflicts found — plan aligns with all principles.

Detail, principle by principle (`Docs/Constitution.md`):

- **Rendering, Game Logic (ECS), Dependency Injection (VContainer), UI (UI Toolkit), Assembly
  Structure, C# Code Style** — not applicable. This is a Python automation-tooling change under
  `scripts/automation/` plus three markdown lifecycle-doc edits; it touches no Unity project, no `src/`
  ECS code, no VContainer composition root, no UI Toolkit assets, no `.asmdef`, and no C#.
- **Planning Discipline ("Plan before implement")** — satisfied by this plan itself being written and
  approved before any code/doc edit is made; this change is outside both the bot-feature and
  performance-optimization carve-outs, so it follows the standard plan path (this document).
- **Specification Discipline ("Spec before plan")** — satisfied: `spec.md` already exists at
  `Docs/Specs/26_07_31_23_prevent-double-automation/spec.md` and this plan builds directly on it.
- **File Organisation** — satisfied: this plan is saved at exactly
  `Docs/Specs/26_07_31_23_prevent-double-automation/plan.md`, alongside its `spec.md`, per the
  `Docs/Specs/<YY_MM_DD_HH>_<name>/` convention.

Use the implement skill to start working on the plan or request changes.
