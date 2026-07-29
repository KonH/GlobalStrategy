# Plan: Sell Arms Card

## Spec

Add a country-scoped "Sell Arms" card that may be drawn and played only while the selected country is an attacker or defender in an active war and the playing organization has at least 80 opinion with that country's current military advisor. Eligibility is re-evaluated through the ordinary initial-hand, draw, playability, and visual-state condition gates.

Key acceptance criteria:
- A card is never pushed into a full hand when its conditions become true; it only joins the eligible pool on a later normal draw. A held copy remains in hand when war or opinion eligibility is lost, becomes unplayable, and can become playable again when the same country later satisfies both conditions.
- Missing advisor/opinion data counts as 0, exact opinion 80 passes, and changing the active advisor immediately changes eligibility. Attacker and defender countries are treated identically.
- A valid play always succeeds with no roll or cost: the selected country gains 10 percentage points of troop-damage bonus, only the playing organization gains 300 gold, the card is discarded, and its hand slot is refilled normally.
- Each play creates its own bonus contribution: it stays at +10 until the next in-game month boundary, then decays by 1 percentage point per month to 0 without becoming negative. Contributions stack and decay independently, and an already-applied contribution continues after the war ends.
- The card uses the existing country-card UI, with no cooldown or separate war target picker. This slice exposes the temporary bonus resource; applying it to a future battle-damage formula remains out of scope.

## Goal

Implement Sell Arms as a data-driven country action on the existing unified card pipeline, with one shared role-aware/war-aware country-condition context and reusable configured resource effects for the payout and independently decaying country bonus.

## Approach

- Add `CountryActionConditionContext.Build(...)` as a plain query helper in `src/Game.Systems/`, consolidating the duplicated `ExpressionContext` construction in `InitSystem.CreateCountryActionEntities`, `DrawCardSystem.DrawCountryCards`, `ActionPlayability.Evaluate`, and `VisualStateConverter.BuildEntry`. It resolves `ActionDefinition.TargetRole` dynamically, preserves per-card `RelationCardTarget` behavior, and sets a new numeric `IsInWar` field through the existing `Wars.IsInWar` helper.
- Represent the temporary modifier as a hidden, Country-seeded `troops_damage_bonus_percent` `Resource`. Values remain human-readable percentage points (`10.0` means +10%); no battle consumer or top-bar display is added.
- Extend the existing polymorphic effect config with synchronous `CountryResourceModifier` and `OrgResourceGrant` definitions. `CreateActionEffectSystem` applies the immediate country/org mutations and `ResourceChange` notifications, then creates one bounded `[Savable]` monthly `ResourceEffect` per Sell Arms play for independent decay. No new per-tick system or `GameLogic` orchestration is required.
- Add the action/effect/resource config, localized presentation, unplayable reason, and a placeholder `ActionVisualConfig.asset` entry using an existing military-advisor card sprite. All simulation rules stay in `src/`; the Unity-side change remains UI Toolkit presentation only.

## Agent Steps

- [ ] **Add the shared country-action condition context** — Create `src/Game.Systems/CountryActionConditionContext.cs` with `Build(IReadOnlyWorld world, ActionDefinition definition, string orgId, string countryId, int cardEntity = -1)`. Populate `Control`, `TotalCountryControl`, `HasSuitableRelationTarget`, and default/per-card `RelationStillExists`; resolve `Opinion` from `definition.TargetRole` via `CharacterQuery.GetTargetCharacterByCountryAndRole` plus `ResourceQuery.GetValue`, defaulting to 0; and populate `IsInWar` through `Wars.IsInWar`.

- [ ] **Extend the expression DSL and use the helper at every country-card gate** — Add `ExpressionContext.IsInWar` and the `"isInWar"` evaluator case in `src/Game.Configs/ExpressionNode.cs`. Replace the four duplicated contexts in `src/Game.Main/InitSystem.cs`, `src/Game.Systems/DrawCardSystem.cs`, `src/Game.Systems/ActionPlayability.cs`, and `src/Game.Main/VisualStateConverter.cs` with the helper, building per definition/card so target role and relation target remain current. In `VisualStateConverter.BuildEntry`, map a failed condition containing `isInWar` to `"war_ended"`; add the corresponding lookup in `Assets/Scripts/Unity/UI/CountryActionsView.cs`.

- [ ] **Define and seed the hidden troop-damage bonus resource** — Add `ResourceDefinitions.TroopsDamageBonusPercent = "troops_damage_bonus_percent"` and a Country-seeded, zero-initialized, no-default-effect row in `Assets/Configs/resource_config.json`, leaving it out of `displayWhitelist`. Update `InitSystem.CreateCountryResourceEntities` to explicitly allow this supported Country resource and retain its configured `DefaultInitialValue`; otherwise the current unsupported-resource guard would throw during startup.

- [ ] **Add the two configured action-effect types** — In `src/Game.Configs/EffectConfig.cs`, add `CountryResourceModifierEffectParams` (`string ResourceId`, `double InitialValue`, `double DecayPerMonth`) and `OrgResourceGrantEffectParams` (`string ResourceId`, `double Amount`), and register `"CountryResourceModifier"` and `"OrgResourceGrant"` in `ActionEffectDefinitionListConverter`.

- [ ] **Apply Sell Arms effects through the existing action pipeline** — Extend `src/Game.Systems/CreateActionEffectSystem.cs` so a successful country resource modifier locates the selected country's seeded resource, adds `InitialValue`, emits a country-owned `ResourceChange`, and creates a Country-owned monthly `ResourceEffect` with `Value = -DecayPerMonth`, `MaxTotal = InitialValue`, and `ClampToZero = true`. Give each decay source a deterministic unique id containing the org, country, played card entity, and current tick. Handle the org grant by adding `Amount` to the acting org's existing resource and emitting an org-owned `ResourceChange`. Fail fast with contextual errors if the required seeded country or organization resource is missing; do not create another system or call a system entry point.

- [ ] **Add the Sell Arms data definitions** — In `Assets/Configs/action_config.json`, add Standard country action `sell_arms` with `targetRole: "military_advisor"`, three copies, empty cost, required `gte(isInWar, 1)` and `gte(opinion, 80)` conditions, and effect ids `sell_arms_damage_bonus_effect` and `sell_arms_gold_grant_effect`. In `Assets/Configs/effect_config.json`, configure the country modifier as +10 with 1/month decay on `troops_damage_bonus_percent`, and the org grant as +300 `gold`.

- [ ] **Add card presentation, payout animation, and localization** — Add `action.sell_arms.*`, both `effect.sell_arms_*.*` pairs, and `action.country.unplayable.war_ended` to `Assets/Localization/en.asset` and `ru.asset`, using short practical descriptions and the repository localization workflow for real Russian translations. Add `sell_arms` to `Assets/Configs/ActionVisualConfig.asset`, reusing the existing `letter_of_commendation_military_advisor` front sprite as placeholder art. Update `CardPlayAnimator` so gold barriers are created only for the player organization's `ResourceChange` and `PlayCountrySequence` releases or cancels its `"gold"` barrier alongside control/opinion barriers; add no cooldown label, picker, panel, or dedicated artwork.

- [ ] **Implement the automated coverage below** — Extend the existing focused suites and add small dedicated helper/feature tests where noted, reusing the real `Wars`, action pipeline, resource, card-hand, and visual-state APIs rather than duplicating their behavior in test-only code.

- [ ] **Validate the implementation** — Load the edited JSON configs through the existing config loaders, run the full `src/GlobalStrategy.Core.sln` build and test suite with the repository `dotnet-build`/`dotnet-test` skills, then refresh Unity and confirm the console is free of compile/config errors when an Editor connection is available.

## User Steps

### 1. Verify Sell Arms in Play Mode

With a country selected, use the debug commands to start/end a war and raise the current military advisor's opinion from below to above the threshold (the exact-80 boundary is covered automatically). Confirm the card only appears through normal draws when both gates pass, becomes visibly unavailable without leaving the hand when the war ends, and becomes available again if the country re-enters a war. Play it as both attacker and defender; confirm the organization gains 300 gold and the displayed total remains correct after the card animation and a later tick, the card is replaced normally, the reused placeholder artwork renders, and no cooldown or target picker appears.

## Tests

- `src/Game.Tests/ExpressionNodeTests.cs` — assert `"isInWar"` returns its context value and composes with `gte`.
- New `src/Game.Tests/CountryActionConditionContextTests.cs` — cover target-role-specific opinion (including high diplomacy/low military), exact 80, missing advisor/resource as 0, active-advisor replacement, relation-card preservation, attacker/defender `IsInWar == 1`, and a non-participant value of 0.
- `src/Game.Tests/ActionPlayabilityTests.cs` — cover both Sell Arms gates, empty-cost playability, exact threshold, held-card behavior across stop/redeclare war, and parity with `CheckActionConditionSystem`/`ActionSucceededSystem`.
- `src/Game.Tests/DrawCardSystemTests.cs` and `src/Game.Tests/InitSystemTests.cs` — prove the card is excluded without war or sufficient military-advisor opinion, is not injected merely because conditions change while the hand is full, and becomes eligible on a later requested draw; retain regression coverage for existing diplomacy cards after context consolidation.
- `src/Game.Tests/VisualStateConverterCountryActionsOpinionGateTests.cs` — assert a held copy reports `war_ended` after `Wars.StopWar`, stays in hand, and produces the same role-aware verdict as the play pipeline.
- `src/Game.Tests/TargetedResourceInitializationTests.cs` and `src/Game.Tests/StringConfigParityTests.cs` — assert every country gets exactly one zero-valued `troops_damage_bonus_percent` resource, it is not display-whitelisted, startup accepts it, and both new polymorphic effect rows deserialize with their configured values.
- New `src/Game.Tests/SellArmsCardTests.cs` (or the existing `UnifiedPipelineTests.cs` if the fixture remains clearer) — drive the successful pipeline end to end and assert only the selected country receives +10, only the acting org receives +300 gold, `ResourceChange` notifications identify the correct owners, one bounded monthly decay effect is created per play, the card is removed, and the vacated slot is redrawn. Cover attacker and defender targets and rejection when stop-war is processed before play in the same tick.
- `src/Game.Tests/ResourceSystemTests.cs` — cover no decay before a boundary, 10→9 after one month, 0 after ten without underflow, continued decay after war end, and multiple independently bounded contributions where one expires while another continues.

## Constitution Check

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
