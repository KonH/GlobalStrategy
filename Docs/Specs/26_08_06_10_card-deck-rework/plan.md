# Plan

## Spec faithful summary

Rework country cards into one persistent country-card pool per organisation, with card playability evaluated against the currently selected country rather than a country baked into ordinary card entities. Relation-targeted cards retain their intrinsic primary/target pair and must match the selected primary country. Drawing ignores conditions and affordability, while playing continues to enforce conditions, gold, cooldown, and selected-country validity. Rename the specified cards/effects/locales to describe their single remaining effect, preserve the existing shared `OpinionModifierEffectParams.SourceId` semantics, and expose one canonical ordered playability result plus `PlayableCountryIds` for every consumer and the sibling UI plan.

## Goal

Make country-card identity, deck ownership, draw behavior, selected-country targeting, condition metadata, and content naming internally consistent without changing org cards or cooldown key semantics.

## Approach

Introduce a savable `CardOwnerType` marker (`Org` or `Country`) on both deck and card entities, replacing `CardDeck.CountryId` as the deck discriminator. Create one org deck and one country deck per organisation; create one ordinary country-card entity per `(org, actionId)`, while relation/revenge cards keep `CountryContext` plus their target component and join the same country deck. `DeckCopies` becomes the uniform draw weight. Retain target-role and feature-availability gating during creation: `improve_secret_advisor_opinion` has no entity when `EnableSecretAdvisor` is false and exactly one per organisation when enabled.

Represent the unified relation operand directly on `ExpressionNode` as `Type = "hasCountryRelation"` plus a validated `RelationKind` string (`none`, `friend`, or `rival`). When `RelationKind = none`, require a validated `DesiredRelationKind` (`friend` or `rival`) so evaluation and player wording distinguish a missing friend candidate from a missing rival candidate without switching on action ids. `ExpressionContext` will carry values by relation kind.

Introduce `ActionPlayabilityResult` as the only playability contract. It contains the full ordered list of structured entries—authored conditions, primary-country match, control-pool capacity, cooldown, and gold exactly once—plus `CanPlay` and `FirstFailure`. Execution, bots, visual projection, playable-country badges, debug view, and failed-play fly text all consume this result rather than recomputing or appending gates.

Keep `PlayCardActionCommand.CountryId` solely as the selected-country validation context and add `SlotIndex` as hand identity. Resolve commands against `(orgId, CardOwnerType, slotIndex, actionId, targetCountryId)` so relation cards with different primary countries but the same target cannot collide. `ActionPlayability` will reject relation/revenge entities whose primary `CountryContext` differs, and every caller will use that single validation path. `VisualStateConverter` will evaluate each country card for the selected country and for every available country in deterministic config order to populate `PlayableCountryIds`.

Materialize the validated selected country in the transient invocation state by adding `CountryId` to `CardUse`. `InitActionFromPlayCardSystem` sets it from the command for country-card plays (empty for org cards); `CheckActionConditionSystem` and `CreateActionEffectSystem` consume that value rather than recovering execution context from the persistent card entity's intrinsic `CountryContext`. Existing cleanup removes `CardUse` after the tick, so ordinary cards stay country-agnostic while effects still target the selected country.

Legacy per-country deck saves, unmarked cards, and renamed action/cooldown ids are intentionally unsupported. Do not add compatibility reconciliation, id remapping, or migration code; current-schema save/load behavior remains covered.

## Agent Steps

- [ ] Add the explicit owner marker in `src/Game.Components/CardOwnerType.cs`; update `src/Game.Components/CardDeck.cs`, `src/Game.Main/InitSystem.cs`, and current-schema save/load-facing tests so each organisation receives exactly one marked org deck and one marked country deck, ordinary country cards have no `CountryContext`, relation cards retain it, and physical `DeckCopies` duplication is removed. Preserve target-role/feature gating so `improve_secret_advisor_opinion` is absent with `EnableSecretAdvisor = false` and appears exactly once per organisation when enabled.
- [ ] Add `SlotIndex` to `src/Game.Commands/PlayCardActionCommand.cs` and thread it from every bot/debug/UI command producer; replace country-id deck lookup with owner-marker lookup in `src/Game.Systems/DrawCardSystem.cs`, `src/Game.Systems/CheckHandSizeSystem.cs`, `src/Game.Systems/RemoveCardFromHandSystem.cs`, and `src/Game.Systems/InitActionFromPlayCardSystem.cs`; resolve hand cards by `(orgId, CardOwnerType, slotIndex, actionId, targetCountryId)`, preserve command `CountryId` only as selected play context, use `DeckCopies` as weight, and exclude `CardDiscard` from every draw candidate query.
- [ ] Extend transient `src/Game.Components/CardUse.cs` with the validated selected `CountryId`; set it in `InitActionFromPlayCardSystem`, consume it in `CheckActionConditionSystem` and `CreateActionEffectSystem` for condition/effect targeting, keep it empty for org cards, and rely on `CleanupActionEffectsSystem` to remove the invocation context with `CardUse` after the tick.
- [ ] Remove condition checks from opening-hand and replacement draws in `src/Game.Main/InitSystem.cs` and `src/Game.Systems/DrawCardSystem.cs`; do not add affordability checks, and retain hand-slot/card-discard lifecycle behavior.
- [ ] Replace the boolean-only playability path in `src/Game.Systems/ActionPlayability.cs` with `ActionPlayabilityResult`, containing an ordered structured entry list, `CanPlay`, and `FirstFailure`; include authored conditions, selected-primary-country validation, control-pool capacity, cooldown, and gold exactly once. Route execution, bot evaluation, visual projection, playable-country enumeration, debug/force-draw lookup, and failed-play feedback through this canonical result.
- [ ] Parameterize relation conditions in `src/Game.Configs/ExpressionNode.cs`, `src/Game.Systems/CountryActionConditionContext.cs`, and `src/Game.Systems/CountryRelations.cs`; replace `hasSuitableRelationTarget` and `relationStillExists` config expressions with `hasCountryRelation` plus validated `RelationKind` values and require `DesiredRelationKind = friend|rival` when `RelationKind = none`.
- [ ] Make every `ActionPlayabilityResult` entry presentation-ready in `src/Game.Configs/ActionConditionDebug.cs`: add locale key/argument data for every authored and synthetic gate (including desired-kind-aware relation text), keep the raw technical label only as optional debug detail, and fail fast on unsupported relation kinds or malformed operands.
- [ ] Update `src/Game.Systems/RelationCardSyncSystem.cs` and `src/Game.Systems/RevengeCardSyncSystem.cs` to enumerate participating organisations crossed with available countries rather than per-country decks; keep one entity per directional relation/revenge pair and the renamed `declare_revenge_war` id.
- [ ] Update `src/Game.Bots/BotObservation.cs` and any bot card query/selection consumers under `src/Game.Bots/` to evaluate each marked country card against each country under consideration instead of requiring every hand entity to contain `CountryContext`.
- [ ] Extend `src/Game.Main/VisualState.cs`, `src/Game.Main/VisualStateConverter.cs`, and `src/Game.Main/StateEquality.cs` with the canonical ordered playability entries, `CanPlay`, `FirstFailure`, and deterministic `PlayableCountryIds`; recompute the unchanged hand against the current selection and project `ActionPlayabilityResult` without appending or duplicating any gate.
- [ ] Apply all action/effect id renames and single-effect deletions in `Assets/Configs/action_config.json`, `Assets/Configs/effect_config.json`, `src/Game.WebClient/wwwroot/configs/action_config.json`, and `src/Game.WebClient/wwwroot/configs/effect_config.json`; retain the current shared `letter_of_commendation` and `royal_audience` opinion-modifier `SourceId` values.
- [ ] Rekey names/descriptions in `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, using concise practical descriptions and real Russian translations; rekey the existing sprite entries in `Assets/Configs/ActionVisualConfig.asset` without changing sprites.
- [ ] Replace renamed hardcoded ids in `src/Game.Main/InitSystem.cs`, `src/Game.Main/VisualStateConverter.cs`, `src/Game.Systems/CountryActionConditionContext.cs`, `src/Game.Systems/RevengeCardSyncSystem.cs`, and all test fixtures; verify no stale old action, effect, or locale ids remain.
- [ ] Update/add focused tests in `src/Game.Tests/InitSystemTests.cs`, `DrawCardSystemTests.cs`, `ActionPlayabilityTests.cs`, `CountryActionConditionContextTests.cs`, `ActionConditionDebugTests.cs`, `RelationCardSyncSystemTests.cs`, `RevengeCardGameLogicTests.cs`, `BotObservationTests.cs`, `ActionCardEntryEqualityTests.cs`, and `StringConfigParityTests.cs`, including relation-card slot disambiguation, ordinary-card selected-country condition/effect targeting through transient `CardUse.CountryId`, cleanup of that invocation context, secret-advisor flag on/off creation, desired relation-kind wording, and exact canonical gate ordering with no duplicates. Do not add legacy snapshot or migration tests.

## User Steps

### 1. None

No Unity Editor scene, prefab, or asset wiring is required; all affected assets are source-controlled config/localization data.

## Tests

- Run the repository `dotnet build` workflow and the full `dotnet test` suite.
- Verify one marked country deck per organisation, stable hand entities across country selection, slot-based disambiguation of relation cards, transient selected-country propagation into ordinary-card conditions/effects, relation primary-country rejection, global `(org, actionId)` cooldown behavior, secret-advisor flag gating, and deterministic `PlayableCountryIds`.
- Verify opening and replacement draws can select failing/expensive cards, honor `DeckCopies` weights, and never select a `CardDiscard` entity.
- Verify the canonical playability result orders every authored and synthetic gate exactly once and is shared by execution, bots, projection, badges, debug, and fly-text consumers.
- Verify each renamed card resolves in desktop and WebClient configs, has exactly its intended effect, and leaves no old ids or locale keys.

## Constitution Check

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
