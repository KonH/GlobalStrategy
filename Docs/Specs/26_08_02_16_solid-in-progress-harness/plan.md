# Plan: Solid `ai-in-progress` Harness Ownership

## Spec

Full spec: `Docs/Specs/26_08_02_16_solid-in-progress-harness/spec.md`.

**Intent:** The Python harness alone owns `ai-in-progress` — set at claim time before work, cleared when the agent CLI returns (success or non-limit failure) — with **no** automatic stale-label reclaim, so a peer instance cannot strip a live run’s label and duplicate work. True hard-kills leave a stuck label for **manual** owner cleanup.

**Acceptance criteria (condensed):**

- Claim won ⇒ `ai-in-progress` is present before checkout / CLI spawn.
- CLI returns (success or non-limit failure) ⇒ harness clears `ai-in-progress` for that item; discovery no longer treats it as live in-progress (parked by outcome label, or plain candidate again).
- Limit/pause path ⇒ clear via existing `handle_limit_pause` / `release_in_progress_silently` (no reclaim markers/counters).
- Peer instance while A’s CLI is live ⇒ B must **not** strip `ai-in-progress` at startup and must not claim that item.
- Failed claim ⇒ skip CLI; claim-path rollback as today; rare claim-only cleanup miss OK.
- Hard-kill after claim, before post-CLI clear ⇒ label may stick forever; owner removes it manually; **no** auto-reclaim.
- Cross-instance race on unlabeled candidate ⇒ keep PR #109 `claim_candidate` (one winner).

**Resolved ambiguity:** Agents never add/remove `ai-in-progress`; they only apply `ai-complete` / `ai-need-attention`.

**Out of scope:** Lease reclaim (abandoned PR #117 / `26_08_02_14_prevent-cross-instance-reclaim`), historical duplicate cleanup, changing claim protocol internals, external locks, Unity.

## Goal

Remove peer-steal reclaim, keep `claim_candidate`, and make every provider wrapper clear `ai-in-progress` for the claimed candidate after CLI return (including Cursor’s `SystemExit` failure path and Claude’s `error_max_turns` salvage path), with docs/tests updated to match harness-only label ownership.

## Approach

1. **Keep** `claim_candidate` and all three wrapper call sites (before `checkout_clean` / CLI). Update its docstring only: drop the “next `reclaim_stale_in_progress` would count this as a crash” wording; keep claim-failure rollback of `ai-in-progress`. **Note (accepted):** non-exception `return False` after `add_label` (rival win, or empty fresh comments) still does not roll back the label — rival win is correct; empty-fresh leftovers used to be healed by reclaim and now need the same owner-manual cleanup as hard-kill sticks. Do not expand claim-protocol changes in this feature.
2. **Delete reclaim machinery** from `scripts/automation/common/issue_handler.py`: `MAX_AUTO_RECLAIMS`, `count_reclaims_since_owner_comment`, `reclaim_stale_in_progress`, and reclaim-marker protocol. Remove startup `reclaim_stale_in_progress(logger, LABEL, MARKER)` from `claude` / `codex` / `cursor` `handle_issues.py` imports and `main`.
3. **Add** `clear_in_progress(logger, number)` beside `release_in_progress_silently`: best-effort `remove_label(number, AI_IN_PROGRESS)` for **one** item only (log success; warn and swallow if already absent — so limit paths that already swept via `release_in_progress_silently` stay safe). Do **not** reuse `release_in_progress_silently` for post-CLI clear (that helper lists/sweeps all provider items).
4. **Wire post-CLI clear** in each wrapper’s candidate loop after the CLI returns, for success and non-limit failure:
   - **Claude** (`scripts/automation/claude/handle_issues.py`): after `run_claude`, if limit → `handle_limit_pause` then `sys.exit(0)` (already clears). Else (including `max_turns_hit` after salvage) call `clear_in_progress(logger, candidate["number"])` before continuing / final `sys.exit`. **Load-bearing:** today max-turns leaves `ai-in-progress` and only re-enters discovery via startup reclaim (`find_candidates` skips the label). After reclaim removal, clear is what makes the item a plain candidate for the next tick — do not keep the leave-label-on model. Rewrite the max-turns header accordingly.
   - **Codex**: same pattern — clear after non-limit CLI return; limit path unchanged via `handle_limit_pause`.
   - **Cursor** (**critical**): today non-zero `returncode` does `raise SystemExit(returncode)` with **no** clear. **Require** `try`/`finally` around per-candidate CLI + limit handling so `clear_in_progress` always runs after CLI return (success, non-zero/`SystemExit`, and limit). Limit path keeps `save_limit_retry_at` + `release_in_progress_silently` then return; rely on best-effort clear in `finally` so a second remove is harmless. Do not use “clear only before `raise SystemExit`” without also clearing on the success fall-through.
5. **Docs rewrite** — kill the false “local flock ⇒ leftover `ai-in-progress` is crash debris / reclaim” assumption; document owner-manual stuck-label recovery; agents never touch `ai-in-progress`:
   - `issue_handler.py` module docstring; `release_in_progress_silently` / `handle_limit_pause` docstrings (drop reclaim-coupling language).
   - Headers of all three `handle_issues.py` files.
   - `.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, `.cursor/commands/cursor-issue.md` — drop “add it yourself” / “then remove `ai-in-progress`”; keep outcome-label steps only; **explicitly forbid** agents adding or removing `ai-in-progress` in Non-goals / owner-transition bullets.
   - `.claude/skills/github-issue-automation/SKILL.md` — replace “Crash recovery: stale-label reclaim…” with harness-owned clear + manual crash recovery; fix Concurrency section that says flock makes reclaim sound; fix per-item lifecycle that still says the CLI adds/removes `ai-in-progress`.
6. **Do not** implement lease reclaim from abandoned `Docs/Specs/26_08_02_14_prevent-cross-instance-reclaim/`.

## Agent Steps

- [ ] **Remove reclaim from shared module** — Delete `MAX_AUTO_RECLAIMS`, `count_reclaims_since_owner_comment`, and `reclaim_stale_in_progress` from `scripts/automation/common/issue_handler.py`. Rewrite the module docstring (drop stale-run reclaim; state harness sets label via `claim_candidate` and clears via post-CLI helper / limit release; flock is same-machine only; stuck labels after hard-kill need owner manual removal). Update `claim_candidate`, `release_in_progress_silently`, and `handle_limit_pause` docstrings to remove reclaim references.
- [ ] **Add `clear_in_progress`** — Implement `clear_in_progress(logger, number)` next to `release_in_progress_silently`: best-effort single-item `remove_label` of `AI_IN_PROGRESS`.
- [ ] **Claude wrapper** — Drop reclaim import/startup call. After each claimed CLI return: if limit → `handle_limit_pause` + `sys.exit(0)` (no extra clear). Else: if `max_turns_hit` → salvage as today; then **always** `clear_in_progress(logger, candidate["number"])` for that candidate — success (`returncode == 0`), non-zero failure, and max-turns alike — before `continue` / final `sys.exit`. Fix header/max-turns comments that still describe reclaim / leaving the label on.
- [ ] **Codex wrapper** — Same: drop reclaim; after non-limit CLI return call `clear_in_progress`; keep `handle_limit_pause` on limit. Rewrite header reclaim section.
- [ ] **Cursor wrapper** — Drop reclaim. **Require** `try`/`finally` around per-candidate CLI + limit handling so `clear_in_progress(logger, candidate["number"])` always runs after CLI return (success, non-zero/`SystemExit`, and limit). Keep limit `save_limit_retry_at` + `release_in_progress_silently` then `return`; rely on best-effort clear in `finally` so a second remove is harmless. Do not use “clear only before `raise SystemExit`” without also clearing on the success fall-through.
- [ ] **Agent lifecycle docs** — Update `.claude/commands/handle-issue.md`, `.codex/skills/codex-issue/SKILL.md`, `.cursor/commands/cursor-issue.md`: wrapper owns `ai-in-progress` entirely; agents apply only `ai-complete` / `ai-need-attention`; remove manual-add fallback and “then remove `ai-in-progress`” / reclaim-crash wording. Also extend Non-goals / “Never remove…” owner-transition bullets to **explicitly forbid adding or removing `ai-in-progress`** (harness-only).
- [ ] **Automation skill doc** — Rewrite `.claude/skills/github-issue-automation/SKILL.md` Concurrency / Crash recovery / lifecycle / limit-note wording that couples flock to reclaim or agents to add/remove `ai-in-progress`.
- [ ] **Tests** — See Tests section; run `scripts/automation/common/test_issue_handler.py` (and any other automation tests touched) to confirm green.
- [ ] **Grep sweep** — Confirm no remaining live references to `reclaim_stale_in_progress`, `MAX_AUTO_RECLAIMS`, `count_reclaims_since_owner_comment`, or “`:reclaim -->`” under `scripts/automation/`, `.claude/`, `.codex/`, `.cursor/` (historical specs/plans may still mention them; leave those alone).

## User Steps

None — no Unity Editor or other hands-on steps.

## Tests

In `scripts/automation/common/test_issue_handler.py`:

- **Remove** `CountReclaimsTests` and `ReclaimStaleInProgressTests` (and imports of deleted symbols / `RECLAIM_MARKER` if unused).
- **Add** `ClearInProgressTests` (or equivalent): happy-path calls `remove_label(number, AI_IN_PROGRESS)`; missing-label / `remove_label` failure is logged and does not raise.
- **Keep** `ClaimCandidateTests`, `release_in_progress_silently` tests, and `handle_limit_pause` tests; only adjust assertions/comments if they mention reclaim.
- No dedicated wrapper `main` tests exist today for reclaim startup (`scripts/automation/test_handle_issues_auto.py` is auto-router only) — do not invent heavy wrapper integration tests unless a thin unit test of the post-CLI clear call order is cheap; shared helper coverage is the primary gate.
- Run the common test module after changes.

## Constitution Check

No conflicts found — plan aligns with all principles. This is Python harness / docs work only (no Unity rendering, ECS, VContainer, UI Toolkit, asmdef, or C# style impact). Spec-before-plan and plan-before-implement are satisfied; output path is `Docs/Specs/26_08_02_16_solid-in-progress-harness/plan.md`.

Use the implement skill to start working on the plan or request changes.
