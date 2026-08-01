# Spec: Prevent Double Automation Work

## Feature Intent

As the repository owner, I want at most one automation instance to ever process a given issue or PR at a time, so that two nearly-simultaneous cron ticks (possibly on separate machines) cannot both work the same item and produce competing branches/PRs.

## Background

`ai-in-progress` is currently applied as the *first action inside* the `claude -p` CLI run (`.claude/commands/handle-issue.md` step 1), which happens well after the wrapper's own discovery (`find_candidates` in `scripts/automation/common/issue_handler.py`) already built its candidate list for that run. The gap between "an item is listed as an unlabeled candidate" and "the label is actually applied" spans the CLI's startup latency plus, for any candidate later than the first in a batch, the full processing time of every earlier candidate in that run. A second automation instance whose own discovery falls inside that gap sees the same item as a candidate and starts its own run on it.

This reproduced on [issue #104](https://github.com/KonH/GlobalStrategy/issues/104): `reclaim_stale_in_progress` removed a stale `ai-in-progress` label at 18:10:05 UTC, and two separate runs both picked the now-unlabeled item up and completed within the same ~5 minute window, opening [PR #105](https://github.com/KonH/GlobalStrategy/pull/105) (`feature/country-view-wars`, 18:14:03 UTC) and [PR #106](https://github.com/KonH/GlobalStrategy/pull/106) (`feature/show-wars-country`, 18:18:47 UTC) for the same feature. The existing `flock` process lock (`acquire_lock`) only prevents concurrent runs *of the same wrapper process on the same machine* — it does nothing for two distinct automation instances (e.g. "instance 0" and "instance 1" on separate schedules/machines), which is the actual scenario reported.

A plain "add label" GitHub API call has no compare-and-swap semantics: calling it twice for the same label is not an error, so simply moving the `add_label(AI_IN_PROGRESS)` call earlier narrows the race window but cannot, by itself, guarantee only one instance wins when two calls land close enough together.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- Two automation instances' discovery runs both find the same open, provider-labeled, unlabeled-status candidate within a short window of each other (including immediately after a stale-run reclaim).
  - Both instances attempt to claim that candidate at roughly the same time => exactly one instance's claim is recognized as the winner => only that instance proceeds to execute the item's prompt.
  - The other instance's claim is recognized as the loser => it does not create a branch, does not commit or push anything, does not create a PR, and does not post a summary comment for that item => no duplicate branch or PR is produced (the exact regression seen on issue #104 with PRs #105/#106).
  - The losing instance backs off cheaply => it leaves the winning claim's `ai-in-progress` label and any of its coordination markers untouched, and simply moves on to its next candidate (or ends its run) => the winning instance's work is not disturbed.
- An instance's claim attempt is uncontested (the common case).
  - It claims the candidate => it proceeds exactly as today: read the prompt, execute it, commit/push, ensure a PR exists (issue candidates), post the summary comment, apply the terminal status label => no behavior change is visible in the non-contention case.
- The claiming mechanism runs before any expensive or irreversible work.
  - An instance has not yet won its claim => it performs no git branch/commit/push operations, no PR creation, and does not begin executing the item's prompt => a lost race costs only a few cheap GitHub API calls, not a full run.
- A crashed run previously left `ai-in-progress` on an item and `reclaim_stale_in_progress` removed it.
  - The item becomes a plain candidate again => the same claim protection applies on its next pickup, identically to a never-claimed candidate => a reclaim cannot reopen the same race that caused issue #104.
- Two instances discover the same batch but race on *different* candidates (not the same item).
  - Each instance independently wins its own candidate's claim => no cross-item interference or blocking between unrelated items.
- The claiming mechanism itself fails or times out unexpectedly (e.g. a `gh` API error while claiming).
  - The instance treats this the same as losing the race => it does not proceed to execute the item's prompt for that candidate => a transient API hiccup never causes two instances to both believe they won.

## Tech Notes

- This is provider-agnostic coordination infrastructure, not Claude-specific — it belongs in the shared `scripts/automation/common/issue_handler.py` (used by the Claude, Codex, and Cursor wrappers), matching how `find_candidates` / `reclaim_stale_in_progress` already live there.
- The claim must move as early as possible in the pipeline — immediately after a candidate is selected for processing and before the (potentially slow) `claude -p` CLI invocation is spawned — rather than remaining the CLI-side "Claim" step in `.claude/commands/handle-issue.md` (and the Codex/Cursor equivalents), which is too late to close the race.
- Because label writes are not compare-and-swap, resolving a genuine simultaneous race needs an additional ordering signal. GitHub issue/PR comments are created with a strict, server-assigned chronological order even under near-simultaneous writers, which is a plausible tie-breaker substrate consistent with this repo's existing "labels + comments are the whole state machine" design (no new external infrastructure, no database, no distributed lock service). The concrete claim protocol (e.g. label-then-verify-via-comment-ordering, or an equivalent) is a decision for `/plan`, not this spec.
- Whatever protocol is chosen must remain compatible with existing reclaim/crash-recovery behavior (`reclaim_stale_in_progress`, `MAX_AUTO_RECLAIMS`) and with the session/usage-limit pause path (`handle_limit_pause`, `release_in_progress_silently`) — both already add/remove `ai-in-progress` and must not be able to defeat the new claim guarantee or vice versa.
- Keep the local `flock` process lock (`acquire_lock`) as-is — it already correctly serializes a single wrapper process's own candidate loop; this fix addresses the separate, cross-instance case.

## Out of Scope

- Implementing the fix (this issue's prompt is `/specify` only — planning and implementation are separate, later steps).
- Redesigning the reclaim/crash-recovery retry-counter logic beyond what compatibility with atomic claiming requires.
- Introducing external locking infrastructure (databases, Redis, a hosted queue, etc.) — the solution must stay within the GitHub API plus the existing local process lock.
- Cleaning up or merging the already-created duplicate PRs #105/#106 from issue #104 — that is a separate, manual/owner decision.
- Changing session/usage-limit pause handling, `auto-ai` provider routing/selection, or provider selection fairness logic beyond what the new claim step requires touching.
