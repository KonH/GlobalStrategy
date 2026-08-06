# Plan

## Spec faithful summary

Rework country cards into one persistent country-card pool per organisation, with card playability evaluated against the currently selected country rather than a country baked into ordinary card entities. Relation-targeted cards retain their intrinsic primary/target pair and must match the selected primary country. Drawing ignores conditions and affordability, while playing continues to enforce conditions, gold, cooldown, and selected-country validity. Rename the specified cards/effects/locales to describe their single remaining effect, preserve the existing shared `OpinionModifierEffectParams.SourceId` semantics, and expose structured condition wording plus `PlayableCountryIds` for the sibling UI plan.

## Goal

Make country-card identity, deck ownership, draw behavior, selected-country targeting, condition metadata, and content naming internally consistent without changing org cards or cooldown key semantics.

## Approach

Introduce a savable `CardOwnerType` marker (`Org` or `Country`) on both deck and card entities, replacing `CardDeck.CountryId` as the deck discriminator. Create one org deck and one country deck per organisation; create one ordinary country-card entity per `(org, actionId)`, while relation/revenge cards keep `CountryContext` plus their target component and join the same country deck. `DeckCopies` becomes the uniform draw weight.

Represent the unified relation operand directly on `ExpressionNode` as `Type = "hasCountryRelation"` plus a validated `RelationKind` string (`none`, `friend`, or `rival`). `ExpressionContext` will carry values by relation kind, so evaluation and debug/presentation metadata do not infer semantics from action ids. Extend `ActionConditionDebugEntry` with a locale key and immutable formatting arguments while retaining its technical label for secondary debugging; selected-country, pool, cooldown, and relation failures use the same structured representation in `ActionCardEntry`.

Keep `PlayCardActionCommand.CountryId` as the selected country at play time. `ActionPlayability` will reject relation/revenge entities whose primary `CountryContext` differs, and every caller will use that single validation path. `VisualStateConverter` will evaluate each country card for the selected country and for every available country in deterministic config order to populate `PlayableCountryIds`.

## Agent Steps

- [ ] Add the explicit owner marker in `src/Game.Components/CardOwnerType.cs`; update `src/Game.Components/CardDeck.cs`, `src/Game.Main/InitSystem.cs`, and save/load-facing tests so each organisation receives exactly one marked org deck and one marked country deck, ordinary country cards have no `CountryContext`, relation cards retain it, and physical `DeckCopies` duplication is removed.
- [ ] Replace country-id deck lookup with owner-marker lookup in `src/Game.Systems/DrawCardSystem.cs`, `src/Game.Systems/CheckHandSizeSystem.cs`, `src/Game.Systems/RemoveCardFromHandSystem.cs`, and `src/Game.Systems/InitActionFromPlayCardSystem.cs`; identify cards by `(orgId, actionId, targetCountryId)`, preserve command `CountryId` only as selected play context, use `DeckCopies` as weight, and exclude `CardDiscard` from every draw candidate query.
- [ ] Remove condition checks from opening-hand and replacement draws in `src/Game.Main/InitSystem.cs` and `src/Game.Systems/DrawCardSystem.cs`; do not add affordability checks, and retain hand-slot/card-discard lifecycle behavior.
- [ ] Add selected-primary-country validation to `src/Game.Systems/ActionPlayability.cs`; route `src/Game.Systems/InitActionFromPlayCardSystem.cs`, bot evaluation, visual projection, and debug/force-draw lookup through the same marked-card and playability contracts.
- [ ] Parameterize relation conditions in `src/Game.Configs/ExpressionNode.cs`, `src/Game.Systems/CountryActionConditionContext.cs`, and `src/Game.Systems/CountryRelations.cs`; replace `hasSuitableRelationTarget` and `relationStillExists` config expressions with `hasCountryRelation` plus validated `RelationKind` values.
- [ ] Make condition presentation structured in `src/Game.Configs/ActionConditionDebug.cs`: add locale key/argument data for every condition type (including kind-aware relation text), keep the raw technical label only as optional debug detail, and fail fast on unsupported relation kinds or malformed operands.
- [ ] Update `src/Game.Systems/RelationCardSyncSystem.cs` and `src/Game.Systems/RevengeCardSyncSystem.cs` to enumerate participating organisations crossed with available countries rather than per-country decks; keep one entity per directional relation/revenge pair and the renamed `declare_revenge_war` id.
- [ ] Update `src/Game.Bots/BotObservation.cs` and any bot card query/selection consumers under `src/Game.Bots/` to evaluate each marked country card against each country under consideration instead of requiring every hand entity to contain `CountryContext`.
- [ ] Extend `src/Game.Main/VisualState.cs`, `src/Game.Main/VisualStateConverter.cs`, and `src/Game.Main/StateEquality.cs` with structured first-failure/condition data and deterministic `PlayableCountryIds`; recompute the unchanged hand against the current selection and include cooldown, affordability, pool-full, and primary-country validation through the normal playability result.
- [ ] Apply all action/effect id renames and single-effect deletions in `Assets/Configs/action_config.json`, `Assets/Configs/effect_config.json`, `src/Game.WebClient/wwwroot/configs/action_config.json`, and `src/Game.WebClient/wwwroot/configs/effect_config.json`; retain the current shared `letter_of_commendation` and `royal_audience` opinion-modifier `SourceId` values.
- [ ] Rekey names/descriptions in `Assets/Localization/en.asset` and `Assets/Localization/ru.asset`, using concise practical descriptions and real Russian translations; rekey the existing sprite entries in `Assets/Configs/ActionVisualConfig.asset` without changing sprites.
- [ ] Replace renamed hardcoded ids in `src/Game.Main/InitSystem.cs`, `src/Game.Main/VisualStateConverter.cs`, `src/Game.Systems/CountryActionConditionContext.cs`, `src/Game.Systems/RevengeCardSyncSystem.cs`, and all test fixtures; verify no stale old action, effect, or locale ids remain.
- [ ] Update/add focused tests in `src/Game.Tests/InitSystemTests.cs`, `DrawCardSystemTests.cs`, `ActionPlayabilityTests.cs`, `CountryActionConditionContextTests.cs`, `ActionConditionDebugTests.cs`, `RelationCardSyncSystemTests.cs`, `RevengeCardGameLogicTests.cs`, `BotObservationTests.cs`, `ActionCardEntryEqualityTests.cs`, and `StringConfigParityTests.cs`.

## User Steps

### 1. None

No Unity Editor scene, prefab, or asset wiring is required; all affected assets are source-controlled config/localization data.

## Tests

- Run the repository `dotnet build` workflow and the full `dotnet test` suite.
- Verify one marked country deck per organisation, stable hand entities across country selection, relation primary-country rejection, global `(org, actionId)` cooldown behavior, and deterministic `PlayableCountryIds`.
- Verify opening and replacement draws can select failing/expensive cards, honor `DeckCopies` weights, and never select a `CardDiscard` entity.
- Verify each renamed card resolves in desktop and WebClient configs, has exactly its intended effect, and leaves no old ids or locale keys.

## Constitution Check

No conflicts found — plan aligns with all principles.

Use the implement skill to start working on the plan or request changes.
