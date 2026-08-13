# Spec: Bot `warUnlock` Feature (Child C)

## Feature Intent

As a developer building AI-controlled organizations, I want a dedicated **`warUnlock`** bot feature that plays shipped unlock cards — `make_rival` and opinion improvers that clear gates for `declare_war` / `sell_arms` — only when those plays sit on a path to a **profitable** future war and win the shared Δorg_score arbitration pool, so bots can open rivalry/opinion doors without blind grinding or stealing the interval from better-scoring control / war plays.

This is **Child C** of umbrella `Docs/Specs/26_08_01_09_bot-war-features/` (issue #83). Intended implement path after Children A–B land: **`/implement-bot-feature`** (still backed by this formal spec+plan per owner request).

**Locked from umbrella:** unlocks compete in the **same** scoring pool as `control` / war features (no separate pre-pass); at most one strategic play per `botDecisionIntervalHours` cycle (Child B); skip unlock when not a path to profitable war or when dominated by a better scored proposal; eval package ownership is **Child F**.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

- Bot has playable `make_rival` naming a country that would unlock a profitable future declare (or improves war options), and gold/opinion gates allow it.
  - Bot proposes / plays `make_rival` for that target when that candidate wins global Δorg_score arbitration => rivalry exists for a subsequent declare path.
- Bot cannot yet play `declare_war` / `sell_arms` because opinion is below card conditions, but has playable `improve_military_advisor_opinion` and/or `improve_ruler_opinion` (and `improve_diplomacy_advisor_opinion` where needed for `make_rival`).
  - Bot proposes / plays those unlock opinion cards when the expected path to a profitable war beats spending the gold elsewhere in the shared scoring pool => multi-step unlock → declare / sell_arms flow is supported.
- Unlock play would not lead toward a profitable war / is dominated by a better scored control or war play.
  - Bot skips unlock (does not propose non-positive path EV; loses arbitration when another proposal scores higher) => no blind opinion grinding.

## Out of Scope

- Children A / B implementation (observation fields; cadence, arbitration, shared discard, intent-aware draw) — **hard dependencies**; this child consumes them.
- Children D / E (`warDeclare`, `warProsecute`) — they play declare / revenge / `sell_arms`; this child only unlocks gates toward those paths.
- Child F eval package knobs / harness budgets / control-only twin gate — **mention registration only**; do not redefine F’s locked eval horizon here.
- Playing `declare_war`, `declare_revenge_war`, `sell_arms`, or force cards.
- Moving `decrease_enemy_control` into war features.
- New action ids, cost/effect rebalance, or privileged foresight beyond Child A observation.
- Owning shared discard / draw selection (Child B).

## Parent / Dependency

- **Umbrella:** `Docs/Specs/26_08_01_09_bot-war-features/spec.md` (Child C + action ownership table + Resolved Decisions).
- **Prerequisite Child A:** `Docs/Specs/26_08_13_09_bot-war-observation/` — rivals, scores, war/occupation/combat inputs for path EV.
- **Prerequisite Child B:** `Docs/Specs/26_08_13_09_bot-war-infra/` — interval cadence, proposal/arbitration pool, shared discard/draw.
- **Downstream:** D/E consume unlocked rivalry/opinion state; F registers `Docs/BotFeatures/warUnlock/`.
- **Standing feature surface:** `IBotFeature` + `BotFeatureRegistry` under `Docs/Specs/26_07_16_14_bot-org-api/` / bot-feature-eval harness; Constitution bot-feature carve-out applies to the implement skill, but this folder remains the owner-requested formal plan.
