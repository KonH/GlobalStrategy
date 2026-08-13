# Spec: Bot `warProsecute` Feature (Child E)

## Feature Intent

As a developer building AI-controlled organizations, I want a `warProsecute` bot feature that plays **`sell_arms` only** on the preferred side of an active war when that play raises estimated win chance enough to improve expected own profit under dual-side natural-peace accounting, so bots prosecute wars toward `org_score` (and gold) without using Ultimatum / Surrender.

This is **Child E** of umbrella `Docs/Specs/26_08_01_09_bot-war-features/` (issue #83).

**Locked from umbrella (owner 2026-08-02 + 2026-08-09 + 2026-08-12):**
- v1 action ownership: **`sell_arms` only** (shipped: **175g**, military advisor opinion **≥80**, **peacetime OK** — no `isInWar` gate).
- Resolution path: raise win % via arms, then **wait for natural peace** — never play `force_war_win` / `force_war_loss`.
- `featureFlags.enableForceWarCards` defaults **false**; force cards are absent from the world when disabled — v1 must not depend on them.
- Dual-side profit: net expected Δ (control shifts + occupation / province-transfer EV + destroy EV + gold) must be **> 0** after both sides’ peace accounting.
- Country destroy is a **scored side effect**, not a hunt mode.
- Gold: share conceptual **`minGoldReserve`** with control; **allow dipping** below reserve for high-EV `sell_arms` when expected profit compensates.
- Depends on **Child A** (observation) + **Child B** (interval / arbitration / shared discard / intent draw). Eval packages are **Child F**.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

- Active war where the bot wants its preferred side stronger; `sell_arms` is playable on that side; gold policy allows it; expected profit from raising win % (via arms → natural peace) is positive after dual-side accounting.
  - Bot proposes / plays `sell_arms` on the preferred side so estimated win % rises via the shared `WarWinChanceEstimator` path (with pending sell_arms damage bonus), then relies on **natural peace** for resolution => temporary damage bonus improves win chance without force cards.
- `featureFlags.enableForceWarCards` is false (shipped default) so `force_war_win` / `force_war_loss` entities are not created (or are present but ignored).
  - `warProsecute` does **not** play, propose, or score Ultimatum / Surrender for v1 => first ship works with force cards absent or ignored.
- Expected dual-side peace EV after arms is non-positive (or dominated by a better scored play in Child B arbitration).
  - Feature skips / loses arbitration => no “play sell_arms because IsPlayable” behaviour.
- Natural peace can empty a country’s provinces.
  - Destroy (`IsDestroyed`, control cleared) is included in profit EV when it hurts the lead rival more than it hurts the bot => destroy is an allowed scored side effect.
- Other in-war non-force tools that later prove score-relevant may join this feature later; **v1 acceptance centers on `sell_arms`**.

## Out of Scope

- Children A–D and F implementation (consume A/B; share profit math with D; F owns eval configs under locked knobs).
- Bot play of **`force_war_win` / `force_war_loss`** even if `enableForceWarCards` is later turned on (separate follow-up).
- New war gameplay, new action ids, or rebalancing `sell_arms` / peace formulas (consume shipped config: gold **175**, opinion **≥80**, effect `sell_arms_damage_bonus_effect` **+30** `troops_damage_bonus_percent` with **2.0**/month decay).
- Dedicated hunt-destroy mode; moving `decrease_enemy_control` into war features.
- Peacetime-only “pre-buff with no active war” as a v1 acceptance requirement (card remains peacetime-legal; **v1 proposes only when the acting country is at war**).

## Parent / Dependency

- **Umbrella:** `Docs/Specs/26_08_01_09_bot-war-features/spec.md` (Child E + Resolved Decisions + Tech Notes).
- **Prereqs:** `Docs/Specs/26_08_13_09_bot-war-observation/` (A), `Docs/Specs/26_08_13_09_bot-war-infra/` (B).
- **Sibling profit model:** Child D `warDeclare` — share dual-side natural-peace EV helper; prosecute scores **ΔEV from win-% lift**, not full war-start EV.
- **Standing bot surface:** `Docs/Specs/26_07_16_14_bot-org-api/`, `Docs/Specs/26_07_16_14_bot-feature-eval-harness/` (`/implement-bot-feature` carve-out for `IBotFeature` + registry + `Docs/BotFeatures/` only; estimator / observation extras still follow this plan).
