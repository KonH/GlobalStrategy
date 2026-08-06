# Plan: Sell Arms During Peacetime

## Spec

Source: `Docs/Specs/26_08_04_17_sell-arms-peacetime/spec.md` (owner clarifications locked).

As a player, draw and play Sell Arms while a country is at peace (military-advisor opinion ≥ 80), so the temporary troop-damage bonus raises Declare War / Revenge win-chance previews and strengthens any war that starts while the bonus is still decaying.

Acceptance criteria (condensed):
- **Peacetime + opinion ≥ 80** — Sell Arms may be drawn and played; +10 troops-damage bonus; 200 gold paid; hand slot refills normally.
- **Peacetime + opinion < 80** — not drawable; held copy unplayable for insufficient-opinion (not war-ended).
- **Wartime + opinion ≥ 80** — still drawable/playable as today.
- **Held copy across war start/end** — stays in hand; war state alone no longer gates playability; low opinion still blocks via opinion reason only.
- **Preview / combat coupling** — peacetime play refreshes Declare War / Revenge win-% via live damage; bonus survives into a later war and keeps monthly decay after peace.
- **Unchanged** — cost 200 gold, damage-bonus-only effects, +10 stack / 1-pt-per-month decay, no localization or Sell Arms UI chrome work.

Out of scope: opinion threshold / cost / magnitude / decay / deck count changes; reintroducing gold grant; DamageCollector / WarWinChanceEstimator / battle formula changes; Ultimatum/Surrender `isInWar`; bots; new UI hints.

## Goal

Remove only the wartime gate from `sell_arms` in live action config, keep the military-advisor opinion ≥ 80 gate and existing effect/cost, and revise tests so peacetime eligibility and live-damage coupling match the approved behavior.

## Approach

Config-only product change. Draw, playability, and Actions-panel visual state already evaluate `sell_arms` conditions from config (`CountryActionConditionContext.Build`, `DrawCardSystem`, `ActionPlayability`, `VisualStateConverter`). Deleting `gte(isInWar, 1)` is sufficient; no C# gate logic, collector, or estimator changes.

| Layer | Change |
|---|---|
| `Assets/Configs/action_config.json` | On `sell_arms`: remove the `gte(isInWar, 1)` condition. Keep `gte(opinion, 80)`, cost gold 200, `effectIds: ["sell_arms_damage_bonus_effect"]`. Leave `isInWar` on other cards untouched. |
| `src/Game.Tests/` | Update fixtures/assertions that hardcode wartime Sell Arms; add peacetime coupling via same-tick settle + war-survives bonus (see Tests). Prefer `SettleCombatResourcesOnActionTests` over new `DamageCollector` multiplier cases. |
| Effects / combat / preview | No changes — `sell_arms_damage_bonus_effect`, `DamageCollector`, `WarWinChanceEstimator`, `WarBattleSystem` already read live resources. |
| Localization / Sell Arms UI | No changes (desc already peacetime-neutral; `war_ended` mapping remains for other wartime cards). |

## Agent Steps

- [x] **Drop wartime condition on live `sell_arms`** — In `Assets/Configs/action_config.json`, delete only `{ "type": "gte", "members": [ { "type": "isInWar" }, { "type": "value", "value": 1 } ] }` from the `sell_arms` entry. Leave opinion ≥ 80, gold 200, and `sell_arms_damage_bonus_effect` unchanged. Do not edit `ultimatum` / `surrender` / other `isInWar` cards, `effect_config.json`, or localization assets.

- [x] **Update config parity** — `StringConfigParityTests.sell_arms_action_deserializes_with_required_conditions_and_effects`: assert a single opinion ≥ 80 condition (no `isInWar`); keep cost / effectIds assertions.

- [x] **Update playability fixtures + cases** — `ActionPlayabilityTests`: remove `isInWar` from the `sell_arms` fixture in `BuildActionConfig`. Rewrite `sell_arms_requires_active_war_and_military_advisor_opinion` for opinion-only gating (fail below 80 with or without war; pass at ≥ 80 in peacetime). Drop war setup from `sell_arms_is_playable_at_exact_opinion_threshold_without_gold` (or keep war only if proving wartime still works). Rewrite `held_sell_arms_card_stays_in_hand_and_tracks_current_war_state` so war start/end with opinion ≥ 80 leaves the card playable; optionally cover opinion < 80 → unplayable for opinion, not war. Keep `sell_arms_playability_matches_condition_pipeline` aligned with the new fixture.

- [x] **Update draw fixtures + cases** — `DrawCardSystemTests`: remove `isInWar` from the `sell_arms` fixture. Rewrite `draw_skips_sell_arms_without_war_or_sufficient_military_opinion` so peacetime + opinion ≥ 80 draws, and only insufficient opinion (and similar) skips. Rewrite `sell_arms_becomes_eligible_on_later_requested_draw` so eligibility flips on military-advisor opinion (e.g. start below 80 → first draw skips; raise to ≥ 80 → second `CardDraw` succeeds), not on declaring war. Do not collapse this into a single first-draw success case (that belongs in the rewritten skip/draw test).

- [x] **Update visual-state fixtures + cases** — `VisualStateConverterCountryActionsOpinionGateTests`: remove `isInWar` from the `sell_arms` fixture. Replace `sell_arms_reports_war_ended_without_removing_held_card` with a peacetime case: held copy stays in hand; with opinion ≥ 80 it is playable (war state irrelevant); with opinion < 80 it is unplayable via the ordinary opinion reason (not `war_ended`). Keep `sell_arms_visual_and_play_pipeline_use_military_advisor_opinion` consistent (fixture may stop declaring war in `BuildWorldWithSellArmsCard` if war is no longer required).

- [x] **Add peacetime coupling coverage** — Prefer extending `SettleCombatResourcesOnActionTests` (peacetime play → live `damage` same tick) and asserting win-% from that live damage; **require** leftover `troops_damage_bonus_percent` after a later `DeclareWar` (war start does not clear/reset it). Do not add another `DamageCollector` bonus-multiplier unit test. Avoid a parallel Sell-Arms preview formula.

- [x] **Run targeted tests** — Execute the touched test classes (and any new file) via the repo `dotnet-test` skill; fix regressions from fixture drift.

## User Steps

### 1. Peacetime smoke check in Unity Editor

In a play session, select a peaceful country with military-advisor opinion ≥ 80. Confirm Sell Arms can appear in hand and play for 200 gold, that Declare War / Revenge win-% badges refresh upward after play and again as the bonus decays, and that a war started while bonus remains uses the stronger damage. Spot-check opinion < 80 still blocks with the ordinary opinion unplayable reason (not war-ended).

## Tests

- **`StringConfigParityTests`** — `sell_arms` conditions = opinion ≥ 80 only; cost 200 gold; effectIds `["sell_arms_damage_bonus_effect"]`.
- **`ActionPlayabilityTests`** — fixture without `isInWar`; peacetime playable at opinion 80; unplayable below 80 regardless of war; held card stays in hand across war transitions without war gating playability.
- **`DrawCardSystemTests`** — fixture without `isInWar`; peacetime + opinion ≥ 80 eligible to draw; insufficient opinion skipped; eligibility no longer waits on declaring war.
- **`VisualStateConverterCountryActionsOpinionGateTests`** — fixture without `isInWar`; peacetime held card not `war_ended`; opinion failure uses existing opinion unplayable path; card remains in hand.
- **New or extended coupling cases** (prefer `SettleCombatResourcesOnActionTests` / `WarWinChanceEstimatorTests` patterns; decay already in `SellArmsEffectTests`):
  - Prefer: peacetime damage-bonus play → same-tick live `damage` settles (reuse/extend `playing_a_damage_bonus_card_settles_damage_the_same_tick`), and win-% rises when live damage includes the bonus.
  - Required: peacetime bonus applied → `DeclareWar` → `troops_damage_bonus_percent` still at the remaining value (war start does not clear it).

## Constitution Check

No conflicts found — plan aligns with all principles.

- **ECS for game logic:** no MonoBehaviour domain rules; product change is config + tests; combat/preview stay in existing `src/` systems.
- **UI Toolkit / VContainer / assemblies:** no UI, DI, or asmdef changes.
- **Spec before plan / Docs/Specs organisation:** plan sits beside the approved spec in `Docs/Specs/26_08_04_17_sell-arms-peacetime/`.
- **C# style:** any new test code follows project conventions.

Use the implement skill to start working on the plan or request changes.
