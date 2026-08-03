# Plan: Solid `ai-in-progress` Harness Ownership

## Note: concurrent plan drafts

While this plan was being written, a second automation instance also pushed a plan commit
to `feature/solid-in-progress-harness` (`6b17666`) — another live instance of the race this
feature fixes. Both drafts agree on reclaim removal + post-CLI `clear_in_progress` + harness-only
agent docs. This document is the reconciled final: it keeps the plan-review fixes from this run
(especially retiring the provider-wide limit scan that still peer-steals) and adopts useful notes
from the peer draft (empty-fresh claim leftovers after reclaim removal; Ambiguity 0 already resolved
in `spec.md`).

## Spec

Full spec: `Docs/Specs/26_08_02_16_solid-in-progress-harness/spec.md`.

As the repository owner running multiple automation instances, the Python harness itself must set `ai-in-progress` immediately before work starts (already done by merged PR #109 `claim_candidate`) and clear it when the agent CLI returns (success or failure), with **no** automatic stale-label reclaim — so a peer instance cannot strip a still-running run's label and duplicate work, while true process crashes are left for manual cleanup.

**Ambiguity (resolved, binding):** Owner answer **0** — agents must **not** add or remove `ai-in-progress`. Only the Python wrapper owns that label. Agent lifecycle docs must stop instructing agents to claim or clear it. Agents still apply `ai-complete` / `ai-need-attention` only. `spec.md` Ambiguities already records this resolution; implementers must not re-open belt-and-suspenders agent clears.

Acceptance criteria (condensed — see spec.md for the full `Precondition => Action => Outcome` table):

- After a successful claim, `ai-in-progress` is present before any branch checkout or agent CLI spawn for that candidate.
- After the agent CLI returns (success or non-limit failure), the harness clears `ai-in-progress` for that item — including Cursor `SystemExit` and Claude `error_max_turns` paths that today leave the label stuck.
- Session/usage-limit pause clears `ai-in-progress` for **the claimed item only** (no provider-wide scan that can peer-steal a sibling instance's live label).
- Peer instances must not strip a live run's `ai-in-progress` at startup and must not process that item while the label remains (no peer-steal of the kind verified on #111/#112).
- Failed claims skip without CLI work; claim-path rollback remains acceptable.
- Hard-kill after claim and before post-CLI cleanup leaves `ai-in-progress` stuck until the owner manually removes it — no auto-reclaim.
- Cross-instance discovery race on an unclaimed candidate: existing `claim_candidate` behavior unchanged (exactly one winner).

Out of scope: cleaning historical duplicate PRs/branches; changing claim-race protocol internals; reintroducing reclaim/leases; Unity/game work; external locking; implementing beyond this plan.

**Owner / repo note (not a code requirement):** PR #117 (lease reclaim) is superseded/abandoned — close it when convenient. Spec folder `Docs/Specs/26_08_02_14_prevent-cross-instance-reclaim/` is abandoned; do not implement from it.

## Goal

Remove stale-label reclaim entirely; keep `claim_candidate` as the sole cross-instance exclusion; make every provider wrapper clear `ai-in-progress` for the claimed item after that item's CLI returns (including exits that today bypass cleanup); rewrite agent/harness docs so only the Python wrapper owns add/remove of `ai-in-progress`.

## Approach

### Keep claim; delete reclaim

- **Keep** `claim_candidate(...)` behavior and per-candidate call sites in `scripts/automation/claude|codex|cursor/handle_issues.py` (claim before `checkout_clean` / CLI). Do not change settle delay, claim-comment protocol, or winner/loser semantics.
- **Accepted leftover after reclaim removal:** non-exception `return False` after `add_label` (rival win, or empty fresh claim comments) still does not roll back the label on a true rival win (correct); empty-fresh leftovers that reclaim used to heal now need the same owner-manual cleanup as hard-kill sticks. Do not expand claim-protocol changes in this feature.
- **Delete** from `scripts/automation/common/issue_handler.py`:
  - `reclaim_stale_in_progress`
  - `count_reclaims_since_owner_comment`
  - `MAX_AUTO_RECLAIMS`
  - reclaim-marker protocol (`<!-- …:reclaim -->`) and all call sites / imports
- **Remove** startup `reclaim_stale_in_progress(logger, LABEL, MARKER)` from all three wrappers (`claude` ~L255, `codex` ~L240, `cursor` ~L170).
- After deletions, `rg` for reclaim leftovers under `scripts/automation`, `.claude`, `.codex`, `.cursor` and purge every hit (wrapper headers, Claude max-turns salvage comment, skill intros, agent “reclaim logic” sentences, `claim_candidate` / limit-pause docs). Historical specs/plans may still mention reclaim — leave those alone.

### Shared post-CLI / limit clear helper

Add a small shared helper next to the existing release helpers in `issue_handler.py`, e.g.:

```text
clear_in_progress(logger, number) -> None
```

- Calls `remove_label(number, AI_IN_PROGRESS)` for **that item only** (not a scan of all provider-labeled items — multi-candidate / multi-instance must not clear other live claims).
- Best-effort / idempotent: if the label is already gone (limit path already cleared, gh 404), log a warning and continue — never raise into the wrapper's exit path. This makes try/finally safe after an earlier clear.

**Limit paths must also become per-item.** Today's `release_in_progress_silently(logger, label)` lists every provider-labeled item with `ai-in-progress` and clears all of them. That was only “safe” under the false flock ⇒ sole-live-run assumption. After reclaim is gone, instance B hitting a limit on #101 would strip instance A's live claim on #100 — recreating peer-steal on a different path.

- Change `handle_limit_pause` (clean/committed path) and Cursor's limit path to `clear_in_progress(logger, candidate["number"])` — matching the salvage-failure branch's existing per-number `remove_label`.
- Delete `release_in_progress_silently`, or reimplement it as a thin one-number wrapper/alias of `clear_in_progress` if call-site churn is smaller that way — **do not keep a provider-wide scan**.

### Wrapper call-site placement

For each candidate in all three wrappers:

1. `claim_candidate` → on `False`, `continue` (no clear; claim rollback owns failure).
2. **Only after a True claim**, enter `try:` checkout + CLI + limit/max-turns handling.
3. `finally: clear_in_progress(logger, candidate["number"])`.

**CRITICAL:** never put `claim_candidate` (or a lost-claim `continue`) inside that `try`. `continue` inside `try` still runs `finally` and would clear the **winner's** live label — recreating #111/#112-style steal via claim placement.

**Cursor (critical gap today):** `raise SystemExit(returncode)` on non-zero (~L189–190) currently exits **without** clearing the label. One `try`/`finally` must wrap checkout + `run_cursor` + limit handling + `raise SystemExit(returncode)` so both `SystemExit` (not caught by Cursor's outer `except Exception`) and ordinary `Exception`s clear before outer handling. Limit path: per-number clear, then `return` (finally still runs; second clear is best-effort).

**Claude:** After CLI return, limit → `handle_limit_pause` + `sys.exit(0)` (clears claimed number; finally best-effort). Non-limit including `max_turns_hit` salvage: keep salvage, then finally clears `ai-in-progress`. **Load-bearing:** today max-turns leaves `ai-in-progress` and only re-enters discovery via startup reclaim (`find_candidates` skips the label). After reclaim removal, clear is what makes the item a plain candidate for the next tick — do not keep the leave-label-on model. Non-zero `exit_code` at end of loop still clears per item in finally before `sys.exit(exit_code)`.

**Codex:** Same try/finally pattern as Claude for limit vs non-limit; no max_turns path today.

Limit paths must **not** go through reclaim-shaped comments.

### Doc / comment rewrites (harness-only label ownership)

Rewrite false "local flock ⇒ leftover `ai-in-progress` is crash debris ⇒ reclaim" assumptions:

| Surface | Change |
| --- | --- |
| `issue_handler.py` module docstring | Drop stale-run reclaim section; document harness claim + post-CLI `clear_in_progress`; crash recovery = owner manually removes `ai-in-progress`; flock remains same-machine only. Soften limit-note wording that says "cannot leave ai-in-progress for reclaim." |
| `claim_candidate` docstring | Keep rollback-on-exception behavior; remove "next `reclaim_stale_in_progress` would wrongly count as a crash" — say stuck labels need manual cleanup instead. |
| Limit-pause helpers | Document per-number clear only; remove reclaim-counter / reclaim-marker / scan cross-references. |
| `claude` / `codex` / `cursor` `handle_issues.py` headers | Delete "Stale-run reclaim" paragraphs and "locking/reclaim" / "reclaimed as stale" wording; Claude max_turns text (header + inline salvage comment ~L284) must stop saying the item stays in-progress for reclaim — harness clears after CLI return; stuck labels only after hard-kill. |
| `.claude/commands/handle-issue.md` | Hand-off = apply exactly one of `ai-complete` / `ai-need-attention` only. Add Non-goal: never add or remove `ai-in-progress` (wrapper owns it). Delete "then remove `ai-in-progress`" and the "crashed run to … reclaim logic" sentence. |
| `.codex/skills/codex-issue/SKILL.md` | Same harness-only rule: drop "Claim — add it yourself"; forbid add/remove of `ai-in-progress` even for manual runs (operator/wrapper manages the label); outcome labels only. |
| `.cursor/commands/cursor-issue.md` | Same as Codex (drop manual add + agent remove; forbid touching `ai-in-progress`). |
| `.claude/skills/github-issue-automation/SKILL.md` | Replace "Crash recovery: stale-label reclaim…" with manual stuck-label recovery; update per-item lifecycle (wrapper claims/clears; agent applies outcome labels only); fix flock text that claims reclaim is what makes locking "sound"; fix intro "Discovery/locking/reclaim" wording. |

Agents still must end with exactly one of `ai-complete` / `ai-need-attention`. An item with neither after harness clear is eligible again as a plain candidate (accepted).

### Unchanged

- Local `acquire_lock` / flock
- `find_candidates` skip rules
- Claim race internals (beyond deleting reclaim references in comments/docs)
- Limit detection, salvage, auto-ai reroute (beyond switching limit clear to per-item and removing reclaim coupling in docs/comments)

## Section 1 — Agent Steps

- [ ] **Confirm Ambiguity 0 stays resolved in `spec.md`** — Keep the resolved Ambiguities text (agents never add/remove `ai-in-progress`; wrappers own the label). Do not reintroduce belt-and-suspenders agent clears.
- [ ] **Delete reclaim machinery** — Remove `MAX_AUTO_RECLAIMS`, `count_reclaims_since_owner_comment`, and `reclaim_stale_in_progress` from `scripts/automation/common/issue_handler.py`; strip reclaim narrative from the module docstring and from `claim_candidate` / limit-pause comments. Then `rg -n 'reclaim|MAX_AUTO_RECLAIMS|reclaim_stale|locking/reclaim|reclaimed as stale|stale-in-progress reclaim' scripts/automation .claude .codex .cursor` and clear every remaining hit (wrappers including Claude max-turns salvage comment, skill intros, agent lifecycle sentences). Leave historical specs/plans alone.
- [ ] **Add `clear_in_progress` and retire scan release** — Implement per-number best-effort `clear_in_progress(logger, number)`. Change `handle_limit_pause` clean/committed path and Cursor's limit path to clear only `candidate["number"]`. Delete `release_in_progress_silently` or reduce it to a one-number alias — **no provider-wide scan**.
- [ ] **Wire Claude wrapper** — Drop startup reclaim import/call; **after a True claim only**, wrap checkout/CLI/limit/max-turns in `try/finally` that calls `clear_in_progress`; keep `handle_limit_pause` + `sys.exit(0)` on limit; update header docs (including max_turns). Never put `claim_candidate` inside the try.
- [ ] **Wire Codex wrapper** — Same reclaim removal + claim-outside-try + try/finally clear pattern; keep `handle_limit_pause` on limit; update header docs.
- [ ] **Wire Cursor wrapper** — Same reclaim removal; one `try`/`finally` around checkout + `run_cursor` + limit handling + `raise SystemExit(returncode)` (covers `SystemExit` and ordinary `Exception` before outer logging); limit path uses per-number clear then `return`; update header/module docs.
- [ ] **Update agent lifecycle docs (harness-only)** — Edit `.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, `.cursor/commands/cursor-issue.md`, and `.claude/skills/github-issue-automation/SKILL.md`: explicit Non-goal / never add or remove `ai-in-progress`; wrappers own it; outcome labels only; replace reclaim crash-recovery with manual owner removal; drop "add it yourself" and "reclaim logic" sentences.
- [ ] **Update unit tests** — See Tests section: delete reclaim test classes; replace scan-release tests; add `clear_in_progress` + peer-steal regression coverage; adjust imports.
- [ ] **Run tests** — Execute `scripts/automation/common/test_issue_handler.py` (and any wrapper tests touched) and fix failures.

## Section 2 — User Steps

None — no Unity Editor, scene/asset, or visual verification work.

## Tests

Update `scripts/automation/common/test_issue_handler.py` (source-touching change requires tests):

**Remove**

- Entire `CountReclaimsTests` class and its import of `count_reclaims_since_owner_comment`.
- Entire `ReclaimStaleInProgressTests` class and its import of `reclaim_stale_in_progress`.
- `RECLAIM_MARKER` constant if unused afterward.
- Any wrapper-level assertions of startup reclaim (re-grep when implementing).

**Rewrite (limit release must not peer-steal)**

- Replace `release_in_progress_silently` scan tests with coverage that limit/clean release removes **only** the claimed number (fixture with two in-progress items; assert sibling untouched).
- Update `handle_limit_pause` tests that currently patch `release_in_progress_silently` to expect per-number clear instead.

**Keep (unchanged behavior)**

- `ClaimCandidateTests` (including exception rollback that still calls `remove_label`).

**Add (required regressions)**

- `clear_in_progress` unit tests: calls `remove_label(number, AI_IN_PROGRESS)`; swallows/logs failure when remove raises (already-cleared / 404); does not call `list_labeled_items` (guards against accidental scan semantics).
- Limit / clear path with two in-progress items: only the target number is cleared.
- Simulated lost-claim path does **not** call `clear_in_progress` for that number (guards claim-inside-try footgun at the testable boundary — helper not invoked on loss).
- Cursor-style `SystemExit` after a successful claim still invokes `clear_in_progress` for that number (thin wrapper test or extracted loop helper test).
- After reclaim deletion, wrappers do not reference `reclaim_stale_in_progress` (import/grep or import-smoke).

## Constitution Check

Checked against `Docs/Constitution.md`:

- Rendering / ECS / VContainer / UI Toolkit / Assembly Structure / C# Code Style — N/A (Python automation scripts and markdown docs only).
- Planning Discipline — satisfied: this plan precedes implementation.
- Specification Discipline — satisfied: feature work has approved `spec.md`; Ambiguity 0 resolved by owner before this plan.
- File Organisation — plan lives at `Docs/Specs/26_08_02_16_solid-in-progress-harness/plan.md`.

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
