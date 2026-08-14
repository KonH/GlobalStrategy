# Spec: Bot `warDeclare` Feature (Child D)

## Feature Intent

As a developer building AI-controlled organizations, I want a `warDeclare` bot feature that plays shipped `declare_war` and `declare_revenge_war` only when **net expected Δorg_score > 0** after dual-side natural-peace accounting (winner/loser control shifts, occupation/province-transfer EV, destroy EV, gold), preferring high win % via `WarWinChanceEstimator` and softly favoring enemy-higher-control / no-bot-control targets — so bots start (or rematch) wars as scored levers rather than “playable ⇒ play.”

This is **Child D** of umbrella `Docs/Specs/26_08_01_09_bot-war-features/` (issue #83). Implementation is an `/implement-bot-feature` surface (`IBotFeature` + registry) that **depends on Children A + B**. Eval packages / merge gate live in **Child F**.

**Locked from umbrella (owner 2026-08-02 + 2026-08-09 + 2026-08-12):**
- Feature id **`warDeclare`** (camelCase like `control`).
- Owns action ids **`declare_war`** (150g) and **`declare_revenge_war`** (125g) — verified against shipped `Assets/Configs/action_config.json`.
- Dual-side profit: net Δorg_score **> 0** after peace control shifts (**winner +50%** / **loser −100%** from `peaceWinnerControlIncreaseFraction` / `peaceLoserControlDecreaseFraction`), occupation/province transfers (**50–80%** band), destroy EV, and gold.
- Prefer high win % via shared **`WarWinChanceEstimator`**.
- Soft scoring bias toward countries where **enemy control is higher** OR **no bot control** is present (still allow declare when raw net Δ wins).
- Revenge: only when still **profitable**; soft arbitration weight **`score = Δ × 1.20`** vs raw Δ for ordinary declare (B owns the weight helper; D proposes revenge with raw Δ > 0).
- Gold: share conceptual **`minGoldReserve`** with control; **allow dipping** below reserve for clearly high-EV expensive declares.
- No Ultimatum / Surrender; natural peace only.

## Acceptance Criteria

Legend: `Precondition => Action => Outcome`.

- Bot has playable `declare_war` and/or `declare_revenge_war` with a named rival/target, can afford it under gold policy, expected net Δorg_score is positive after dual-side accounting (including occupation + enemy control decrease + gold), and `WarWinChanceEstimator` win % meets the chosen threshold; soft target preference favors enemy-higher-control / no-bot-control countries.
  - Feature proposes that card into Child B’s arbitration pool (and it may win the interval play) => war starts (or rematch) with shipped costs/effects.
- Multiple declare instances are playable on the same interval.
  - Feature ranks by highest expected Δorg_score (preferring higher win % when deltas are close), not first-in-hand => best scored declare is proposed.
- Both `declare_revenge_war` and ordinary `declare_war` are score-positive on the same interval.
  - Revenge gets soft **+20%** arbitration weight on its estimated Δ (`score = Δ × 1.20`); ordinary declare uses raw Δ => rematches are preferred when still profitable, without a hard “always revenge” rule.
- Revenge is eligible / playable but rematch expected net Δorg_score is non-positive.
  - Bot does **not** propose or play `declare_revenge_war` solely because it is eligible.
- Edge: declare cards are playable but expected Δorg_score ≤ 0 after dual-side accounting.
  - Feature skips those plays => no “play war because IsPlayable” behaviour.
- Edge: gold after cost would fall below `minGoldReserve`, but expected Δorg_score clearly compensates under the dip rule.
  - Feature may still propose the declare => expensive high-EV wars are not blocked solely by reserve.
- Edge: gold after cost would fall below reserve and the play is not high-EV under the dip rule.
  - Feature does not propose that card.

## Out of Scope

- Children A / B / C / E / F implementation (observation fields, interval/arbitration/discard/draw, `warUnlock`, `sell_arms` prosecute, eval packages) — D **consumes** A+B and is **evaluated** in F.
- Playing or depending on `force_war_win` / `force_war_loss`.
- New war gameplay, action ids, peace formulas, or `action_config.json` / `effect_config.json` / peace-fraction rebalance (bots consume shipped config).
- Privileged foresight beyond Child A observation + seat-visible estimator inputs.
- Moving `decrease_enemy_control` into war features.
- Dedicated hunt-destroy mode (destroy is EV side effect only).
- Changing locked Child F eval knobs.

## Verified shipped economics (plan-time check)

| ActionId | Gold | Notable gates (config) |
|----------|------|-------------------------|
| `declare_war` | **150** | `targetRulerOrMilitaryOpinion` ≥ 50; rival relation; `neitherSideAtWar` |
| `declare_revenge_war` | **125** | control ≥ 20; mil opinion ≥ 25; `warFree`; `revengeEligible` |

Peace / transfer ( `game_settings.json` ): `peaceWinnerControlIncreaseFraction` **0.5**, `peaceLoserControlDecreaseFraction` **1.0**, `peaceProvinceTransferMinPercent` **50**, `peaceProvinceTransferMaxPercent` **80**, `peaceGoldPerMonth` **1000**.

Revenge pending combat bonuses (`declare_revenge_war_effect`): damage **+10%**, durability **+5%** — passed into win-% estimation when scoring revenge (same pattern as UI preview).

## Parent / Dependency

- **Umbrella:** `Docs/Specs/26_08_01_09_bot-war-features/spec.md` (Child D + Resolved Decisions + profit model).
- **Requires:** Child A observation (`Docs/Specs/26_08_13_09_bot-war-observation/`) and Child B infra (`Docs/Specs/26_08_13_09_bot-war-infra/`).
- **Sibling:** Child E (`warProsecute`) reuses the dual-side peace profit model; Child F owns `Docs/BotFeatures/warDeclare/` eval.
- **Standing bot-feature path:** Constitution bot-feature carve-out + `/implement-bot-feature`; this folder still holds the formal `/specify`+`/plan` pair requested for Child D.
