# Spec: Prevent Cross-Instance Reclaim Steal

## Feature Intent

As the repository owner, I want a second automation instance to never strip `ai-in-progress` and re-queue an item that another instance is still actively processing, so that long-running `/specify`, `/plan`, and `/implement` work is not interrupted mid-flight and does not produce duplicate handoff comments, branches, or PRs.

## Background

[Issue #104](https://github.com/KonH/GlobalStrategy/issues/104) showed two automation instances both discovering and processing the same unlabeled candidate within a short window, producing duplicate PRs ([#105](https://github.com/KonH/GlobalStrategy/pull/105), [#106](https://github.com/KonH/GlobalStrategy/pull/106)). [Issue #108](https://github.com/KonH/GlobalStrategy/issues/108) specified a fix; [PR #109](https://github.com/KonH/GlobalStrategy/pull/109) implemented `claim_candidate` in `scripts/automation/common/issue_handler.py` — a cross-instance claim protocol (label + unique claim comment + settle delay + monotonic comment-id tie-break) that runs immediately after discovery and before any git/CLI work. That closed the **simultaneous-discovery race**: when two instances pick up the same unlabeled candidate at nearly the same time, exactly one wins.

The owner reported on [issue #108](https://github.com/KonH/GlobalStrategy/issues/108) (2026-08-02) that duplicate work still happens — see [issue #111](https://github.com/KonH/GlobalStrategy/issues/111) and [issue #112](https://github.com/KonH/GlobalStrategy/issues/112). Investigation confirms a **separate bug**: the **cross-instance reclaim steal**.

`reclaim_stale_in_progress` (called at each wrapper run start, after local `flock`) assumes that because no other run of *this provider on this machine* can be active, any item still carrying `ai-in-progress` must be leftover from a crash. That assumption is false when two automation instances run on separate machines or schedules (the owner's setup uses a ~5-minute gap between instances). Instance B's reclaim pass therefore strips `ai-in-progress` from items instance A is still working on, making them plain candidates again; instance B then claims and runs the same prompt while A's CLI is still in flight.

Verified timelines match the ~5-minute schedule gap:

- **[Issue #112](https://github.com/KonH/GlobalStrategy/issues/112):** `ai-in-progress` at 13:25:05 (instance A); unlabeled at 13:30:06 (instance B reclaim steal); relabeled at 13:30:08 (B claimed); duplicate `/specify` handoffs at 13:30:34 and 13:33:02. Same pattern during `/plan` at 14:05 → 14:10.
- **[Issue #111](https://github.com/KonH/GlobalStrategy/issues/111):** `ai-in-progress` at 13:30:51; unlabeled at 13:33:26 (reclaim steal); duplicate `/plan` handoffs at 13:36:55 and 13:39:23. Again during `/implement` at 14:12 → 14:18.

A contributing factor: after `claim_candidate` wins, it **deletes** all fresh claim comments. There is then no durable GitHub-side evidence on the item that a live run still owns it — only the shared `ai-in-progress` label, which reclaim treats as crash debris with no further check.

This spec addresses reclaim steal only. The existing simultaneous-discovery claim protocol from PR #109 stays; the fix is additive.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`, grouped under a shared precondition where one applies to several rows.

- Two automation instances run on separate machines or schedules (e.g. ~5 minutes apart); instance A has won a claim and is actively executing an item's prompt (CLI still running, item labeled `ai-in-progress`).
  - Instance B starts its scheduled run and its reclaim pass sees the item => instance B does **not** remove `ai-in-progress`, does **not** post a reclaim comment, and does **not** re-queue the item => instance A continues undisturbed until it finishes, hits a limit pause, or truly crashes.
  - Instance B's discovery for that run => the item is skipped (still in-progress from B's perspective) => instance B does not claim or execute the same item while A's run is live.
- Instance A completes an item normally (prompt finished, terminal status label applied).
  - The active-work lease evidence is cleared as part of release => a later instance's reclaim pass may treat a *future* genuine crash on that item normally (subject to existing reclaim-counter rules) => no stale lease blocks re-processing after legitimate completion.
- Instance A hits a session/usage limit and the wrapper takes the limit-pause path.
  - The lease is cleared when `ai-in-progress` is released (silent release or need-attention escalation) => the item can be picked up again after the limit window without being blocked by a phantom lease => limit-pause behavior remains a planned pause, not a crash, and does not consume reclaim budget spuriously.
- A run genuinely crashed or was killed mid-work (no healthy completion, no limit-pause release) and left `ai-in-progress` plus lease evidence on the item.
  - No instance refreshes the lease for longer than the configured stale threshold => the next reclaim pass removes `ai-in-progress`, posts the reclaim marker, and re-queues the item (respecting `MAX_AUTO_RECLAIMS`) => true crashes are still recovered automatically, as today.
- After a genuine crash, automatic reclaim retries are exhausted (`MAX_AUTO_RECLAIMS` reached).
  - Reclaim still escalates to `ai-need-attention` => repeated real crashes do not burn usage forever, unchanged from current behavior.
- Two instances discover the same unlabeled candidate at nearly the same time (the scenario PR #109 fixed).
  - `claim_candidate` still resolves to exactly one winner => no regression to the issue #104 simultaneous-discovery race.
- An instance wins a claim and begins work.
  - Durable lease evidence remains visible on the item for the duration of active processing (until completion, limit release, or lease age-out) => a peer instance's reclaim pass has a signal to distinguish "live owner" from "crash leftover."
- Optional: an item's prompt run exceeds the stale-lease threshold (very long CLI session).
  - If heartbeat refresh is implemented => the lease stays fresh and reclaim continues to refuse steal => long runs are not interrupted by a scheduled peer instance.
  - If heartbeat is not implemented => reclaim may eventually re-queue the item after threshold age => owner accepts that very long runs may need threshold tuning or heartbeat (see Ambiguities).

## Tech Notes

- **Scope:** Provider-agnostic coordination in `scripts/automation/common/issue_handler.py`, consumed by `scripts/automation/claude/handle_issues.py`, `scripts/automation/codex/handle_issues.py`, and `scripts/automation/cursor/handle_issues.py`. No new external infrastructure.
- **Root cause:** `reclaim_stale_in_progress` (lines ~601–644) gates reclaim solely on local `acquire_lock` / `flock` — valid for same-machine serialization, invalid across instances. Its docstring and module header (lines ~27–29) encode the false assumption.
- **Lease concept:** Treat active ownership of an `ai-in-progress` item as a **lease** backed by durable evidence on the GitHub item (comment marker with timestamp), not merely the label alone. Intended direction:
  - **Establish lease:** When `claim_candidate` wins (or equivalent early claim step), post or retain a lease marker comment (e.g. `{marker_prefix}:lease:` with creation time) that persists for the whole run. Today the winner deletes all `{marker_prefix}:claim:` comments (~lines 695–700) — that deletion is what removes live-work evidence and must change.
  - **Respect lease in reclaim:** `reclaim_stale_in_progress` must consult lease evidence before removing `ai-in-progress`. Reclaim only when: (a) no lease marker exists, or (b) the newest lease marker is older than the stale threshold. Optionally also refuse reclaim when the `ai-in-progress` label itself was applied more recently than the threshold (belt-and-suspenders — see Ambiguities).
  - **Clear lease on release:** Normal completion paths (wrapper applies terminal label and removes `ai-in-progress`), `release_in_progress_silently`, and limit-pause / need-attention escalation in `handle_limit_pause` must delete or invalidate the lease marker when releasing the item, so completed or paused items do not block future work.
  - **Stale threshold:** Must be much larger than the owner's inter-instance schedule gap (~5 minutes). Concrete default is an Ambiguity (45 / 90 / 120 minutes).
  - **Heartbeat (optional):** During long CLI runs, the wrapper or a lightweight background tick could refresh the lease marker (update or post new `:lease:` comment) so active work survives threshold duration. Whether to implement heartbeat vs rely on a long threshold alone is an Ambiguity.
- **Preserve existing claim race resolution:** Keep `claim_candidate`'s label + claim-comment + settle + comment-id tie-break for simultaneous discovery. Lease establishment should compose with the win path (e.g. convert winning claim into lease, or post lease immediately after win) without reopening the discovery race fixed in PR #109.
- **Preserve reclaim counter semantics:** `{marker_prefix}:reclaim` comments and `count_reclaims_since_owner_comment` / `MAX_AUTO_RECLAIMS` (currently `2`) unchanged in intent — only items whose lease has genuinely aged out (or is absent) enter the reclaim path.
- **Compatibility:** `find_candidates` already skips `ai-in-progress` items — no change needed for discovery skip logic once reclaim stops stripping live leases. `handle_limit_pause` ordering (salvage → persist retry → release labels → note) must remain safe: failed `post_comment` must not leave a dangling lease that blocks reclaim forever (lease freshness window provides eventual recovery).
- **Tests:** Extend `scripts/automation/common/test_issue_handler.py` with cases for: live lease blocks reclaim; aged/missing lease allows reclaim; lease cleared on completion/release paths; no regression on `claim_candidate` race tests.

## Out of Scope

- External locking infrastructure (Redis, database, hosted queue).
- Cleaning up duplicate work already produced on [issue #111](https://github.com/KonH/GlobalStrategy/issues/111) and [issue #112](https://github.com/KonH/GlobalStrategy/issues/112).
- Redesigning auto-ai routing, provider selection fairness, or session-limit pause semantics beyond what is required for lease cleanup compatibility.
- Changing the simultaneous-discovery claim protocol (`claim_candidate` tie-break, settle delay, freshness window) except where necessary to leave durable lease evidence after a win.

## Ambiguities

- [NEEDS CLARIFICATION: Stale-lease threshold duration] What age should a lease marker (or label, if used) reach before reclaim is allowed? Candidates include 45, 90, and 120 minutes. The value must exceed the owner's ~5-minute inter-instance schedule gap with comfortable margin, and should reflect typical `/specify` + `/plan` + `/implement` durations. If runs routinely exceed the chosen threshold, should the wrapper post periodic heartbeat refreshes during the CLI invocation, or is a single long threshold sufficient?
- [NEEDS CLARIFICATION: Lease marker shape] Should the winning `:claim:` comment be retained and repurposed as the lease (simplest — one comment type), or should a separate `:lease:` (or equivalent) marker be posted after the claim race resolves? Separate markers keep claim cleanup for race hygiene but add an extra API call; retained claim comments are fewer moving parts but leave `:claim:` tokens visible on the thread until run end.
- [NEEDS CLARIFICATION: Label-age belt-and-suspenders] Should `reclaim_stale_in_progress` also refuse to steal when the `ai-in-progress` label was applied more recently than the stale threshold (even if lease comment lookup fails or is ambiguous), or is the lease comment the sole signal? Label-age alone cannot distinguish instance A's live work from a crash that never posted a lease, but it adds defense-in-depth against comment deletion bugs.
