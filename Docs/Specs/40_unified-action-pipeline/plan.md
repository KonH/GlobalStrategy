# Plan: Unified Action Pipeline

## Spec

**Intent:** Unify org-action and country-action execution into a single ECS component pipeline so that action processing is consistent, testable, and extensible without maintaining two divergent code paths.

**Acceptance criteria (summary):**
- `PlayCardActionCommand(actionId, orgId, countryId?)` replaces `PlayActionCommand` and `PlayCountryActionCommand`; both old command types removed.
- Card entity gets `Action(actionId)`, `OrgContext(orgId)`, optional `CountryContext(countryId)`, `CardUse` during execution.
- `InHand` renamed to `CardInHand`; `ActionCard` and `CountryActionCard` removed; unified card entity with context components.
- Explicit deck entity `CardDeck(orgId, countryId?)` and hand entity `CardHand(handSize)`.
- Eleven-system pipeline: Cleanup → Init → CheckCondition → DeductCost → ActionSucceeded → CreateEffects → DiscoverCountry → RemoveFromHand → CheckHandSize → DrawCard → CleanupCardDiscard.
- Old `IEffect` interface, `ResourceChange` class, `InfluenceAdded`, `CharacterOpinionChange` removed; new `ResourceChange` and `DiscoverCountryEffect` are plain ECS structs.
- `ResourceEffect` gains `AccumulatedTotal` / `MaxTotal` fields for bounded monthly effects.
- Character opinion stored as `Resource` entity with `ResourceOwner(characterId, OwnerType.Character)`; `CharacterOpinion` and `OpinionModifier` components removed.
- `VisualState.LastAction` replaced by `VisualState.LastFrameEffects: VisualEffectCollection`; `VisualStateConverter` maps ECS `ResourceChange` entities to `VisualResourceChangeEffect`.
- `ActionSystem` static class and `ActionResult` struct fully removed.

## Goal

Replace the two divergent `ActionSystem.ProcessPlayAction` / `ProcessPlayCountryAction` monoliths with an eleven-step ECS pipeline where each step is a focused static system class. The pipeline processes every card play — org-scoped or country-scoped — through the same stages, driven by ECS component presence rather than branching inside a single method. All visual effect data flows from ECS entities through `VisualStateConverter`, removing the ad-hoc `List<IEffect>` intermediary entirely.

## Approach

Work bottom-up: define new ECS components first so all downstream code compiles against stable types, then implement each pipeline system in isolation (each is independently testable). Wire the pipeline into `GameLogic.Update` and adapt `VisualStateConverter`. Replace the Unity UI callsites last (only `CardPlayAnimator` needs touching — it reads `LastAction` and pushes the old command types). Remove obsolete types only after every consumer has been migrated. Character opinion migration (resource model) is the most cross-cutting change and is sequenced after the pipeline is working, so it can be done as a self-contained step.

## Agent Steps

- [ ] **Step 1: Add `CardInHand` component** — Create `src/Game.Components/CardInHand.cs` as a `[Savable]` struct with `int SlotIndex`. This is the renamed `InHand` with identical semantics.

- [ ] **Step 2: Add `CardUse` marker component** — Create `src/Game.Components/CardUse.cs` as a plain (non-Savable) marker struct. Added to the card entity during execution; removed by `CleanupActionEffectsSystem` the next frame.

- [ ] **Step 3: Add `Action` component** — Create `src/Game.Components/Action.cs` as a non-Savable struct with `string ActionId`. Added to the card entity by `InitActionFromPlayCardSystem`.

- [ ] **Step 4: Add `OrgContext` component** — Create `src/Game.Components/OrgContext.cs` as a non-Savable struct with `string OrgId`. Added alongside `Action`.

- [ ] **Step 5: Add `CountryContext` component** — Create `src/Game.Components/CountryContext.cs` as a non-Savable struct with `string CountryId`. Added only for country-scoped card plays.

- [ ] **Step 6: Add `ActionValid`, `ActionSucceeded`, `ActionFailed` marker components** — Three files in `src/Game.Components/`: `ActionValid.cs`, `ActionSucceeded.cs`, `ActionFailed.cs`. All plain non-Savable marker structs.

- [ ] **Step 7: Add `CardDiscard` marker component** — Create `src/Game.Components/CardDiscard.cs` as a non-Savable marker struct. Added by `RemoveCardFromHandSystem`; removed by `CleanupCardDiscardSystem` the next frame.

- [ ] **Step 8: Add `CardDraw` component** — Create `src/Game.Components/CardDraw.cs` as a non-Savable struct with `int Count`. Added to the deck entity by `CheckHandSizeSystem`; consumed by `DrawCardSystem`.

- [ ] **Step 9: Add `CardDeck` component** — Create `src/Game.Components/CardDeck.cs` as a `[Savable]` struct with `string OrgId` and `string CountryId` (empty string = org deck). This replaces the implicit deck role previously played by `ActionOwner`.

- [ ] **Step 10: Add `CardHand` component** — Create `src/Game.Components/CardHand.cs` as a `[Savable]` struct with `int HandSize`. Placed on the deck entity (same entity as `CardDeck`) to record the target hand size for that deck.

- [ ] **Step 11: Add new `ResourceChange` ECS struct** — Create `src/Game.Components/ResourceChangeEffect.cs` containing: `public struct ResourceChange { public string EffectId; public string ResourceId; public string OwnerId; public double Amount; }`. This is non-Savable (frame-lifetime). Name file distinctly from the old `Effects.cs` to avoid confusion during the transition.

- [ ] **Step 12: Add `DiscoverCountryEffect` ECS struct** — Add `public struct DiscoverCountryEffect { public string EffectId; }` to `src/Game.Components/ResourceChangeEffect.cs` (same file, same frame-lifetime pattern).

- [ ] **Step 13: Add `OwnerType` enum and extend `ResourceOwner`** — Add `public enum OwnerType { Org, Country, Character }` in `src/Game.Components/ResourceOwner.cs`. Change the `ResourceOwner` primary constructor to `ResourceOwner(string OwnerId, OwnerType OwnerType = OwnerType.Org)`. The default preserves all existing callsites unchanged. When creating character-opinion resource entities (Step 29), pass `OwnerType.Character` explicitly.

- [ ] **Step 14: Add `ResourceEffect.AccumulatedTotal` and `MaxTotal` fields** — Edit `src/Game.Components/ResourceEffect.cs`: add `double AccumulatedTotal` and `double MaxTotal`. `MaxTotal == 0` means unbounded (preserves all existing behaviour). Also add `bool ClampToZero` field (default `false`). When `true`, `ResourceSystem` stops the accumulated value from crossing zero: if applying the monthly amount would change the sign of the resource value, clamp to zero instead and remove the `ResourceEffect` entity.

- [ ] **Step 15: Update `ResourceSystem` to respect `MaxTotal`** — Edit `src/Game.Systems/ResourceSystem.cs`: when applying a `ResourceEffect` that has `MaxTotal > 0`, skip the application if `Math.Abs(AccumulatedTotal) >= MaxTotal`; otherwise add the applied amount to `AccumulatedTotal`. Also handle `ClampToZero`: when `true` and the pending application would change the sign of the target resource's value, apply only enough to reach zero and remove the `ResourceEffect` entity rather than letting it overshoot.

- [ ] **Step 16: Add `PlayCardActionCommand`** — Create `src/Game.Commands/PlayCardActionCommand.cs`: `public struct PlayCardActionCommand : ICommand { public string ActionId; public string OrgId; public string CountryId; }`. `CountryId` is empty string for org-only plays.

- [ ] **Step 17: Verify `PlayCardActionCommand` is picked up by the source generator** — No manual registration is needed. `CommandGenerator` (in `src/Game.SourceGenerators/`) scans the compilation for all types implementing `ICommand` and auto-generates a typed `Read{Name}()` accessor and `Push` dispatch for each. Creating `PlayCardActionCommand : ICommand` in Step 16 is sufficient. After Step 16, run `dotnet build` once to confirm the generated `ReadPlayCardActionCommand()` method appears in `CommandAccessor`. Any code referencing `_commandAccessor.ReadPlayCardActionCommand()` before this build will fail with a missing-method error.

- [ ] **Step 18: Implement `CleanupActionEffectsSystem`** — Create `src/Game.Systems/CleanupActionEffectsSystem.cs`. Static class, `Update(World world)`. Removes `Action`, `ActionValid`, `ActionSucceeded`, `ActionFailed`, `CardUse`, `DiscoverCountryEffect`, and `ResourceChange` (new struct) components from all entities that carry them. Call this at the very start of the pipeline each frame before any other action system.

- [ ] **Step 19: Implement `InitActionFromPlayCardSystem`** — Create `src/Game.Systems/InitActionFromPlayCardSystem.cs`. Static class, `Update(World world, IReadOnlyList<PlayCardActionCommand> commands)`. For each command: if `cmd.CountryId` is empty, query entities carrying `ActionCard` where `ActionCard.OwnerId == cmd.OrgId && ActionCard.ActionId == cmd.ActionId`; add `Action(cmd.ActionId)`, `OrgContext(cmd.OrgId)`, `CardUse` to the found entity. If `cmd.CountryId` is non-empty, query entities carrying `CountryActionCard` where `OrgId == cmd.OrgId && CountryId == cmd.CountryId && ActionId == cmd.ActionId`; add `Action(cmd.ActionId)`, `OrgContext(cmd.OrgId)`, `CountryContext(cmd.CountryId)`, `CardUse`. The component type definitions (`Action`, `OrgContext`, `CountryContext`, `CardUse`) are already created in Steps 3–5. After Step 32 migrates card creation in `InitSystem`, the query switches to `Action + OrgContext [+ CountryContext]`.

- [ ] **Step 20: Implement `CheckActionConditionSystem`** — Create `src/Game.Systems/CheckActionConditionSystem.cs`. Static class, `Update(World world, ActionConfig config)`. Queries entities with `Action` + `OrgContext`. For each: looks up the `ActionDefinition`. Evaluates `Conditions` using `ExpressionContext { Influence = orgInfluence }` (query `InfluenceEffect` entities if `CountryContext` is present). Checks affordability against `Resource` entities by `ResourceOwner(OrgId)`. If all pass, adds `ActionValid`.

- [ ] **Step 21: Implement `DeductActionCostSystem`** — Create `src/Game.Systems/DeductActionCostSystem.cs`. Static class, `Update(World world, ActionConfig config)`. Queries `Action` + `ActionValid`. For each cost in `def.Cost`: deducts from the matching `Resource` entity (by `ResourceOwner(OrgId)` + `resourceId`). Creates a new `ResourceChange` entity: `world.Create()`, add `ResourceChange { EffectId=..., ResourceId=cost.ResourceId, OwnerId=OrgId, Amount=-cost.Amount }`.

- [ ] **Step 22: Implement `ActionSucceededSystem`** — Create `src/Game.Systems/ActionSucceededSystem.cs`. Static class, `Update(World world, ActionConfig config)`. Queries `Action` + `ActionValid`. For each matching entity, unconditionally adds `ActionSucceeded`. `ActionFailed` is never added in the current implementation (dead path reserved for future use).

- [ ] **Step 23: Implement `CreateActionEffectSystem`** — Create `src/Game.Systems/CreateActionEffectSystem.cs`. Static class, `Update(World world, ActionConfig config, EffectConfig effectConfig, DateTime currentTime)`. Queries `Action` + `ActionSucceeded`. For each `effectId` in `def.EffectIds`:
  - `DiscoverCountryEffectParams`: creates a `DiscoverCountryEffect` entity.
  - `InfluenceChangeEffectParams`: creates an `InfluenceEffect` entity (same as current logic, capped at 100 total).
  - `OpinionModifierEffectParams` (character opinion): creates a `ResourceChange` entity targeting `opinion_{orgId}` resource on the character's resource entity; also creates a monthly `ResourceEffect` with `MaxTotal = initialValue` for decay. Set `ClampToZero = true` on the monthly decay `ResourceEffect` to replicate the existing opinion-system behaviour that prevents modifiers from overshooting zero.
  - Resource grant effects (if applicable in future): create `ResourceChange` entities.

- [ ] **Step 24: Implement `DiscoverCountrySystem`** — Create `src/Game.Systems/DiscoverCountrySystem.cs`. Static class, `Update(World world, int proximityEntity, Random rng)`. Queries entities with `DiscoverCountryEffect`. For each: runs the weighted-random proximity selection from the current `ActionSystem.ApplyDiscoverCountry` logic. Adds `IsDiscovered` to the selected country entity.

- [ ] **Step 25: Implement `RemoveCardFromHandSystem`** — Create `src/Game.Systems/RemoveCardFromHandSystem.cs`. Static class, `Update(World world)`. Queries `CardUse` + (`ActionSucceeded` | `ActionFailed`) + `CardInHand`. For each matching entity: remove `CardInHand` and add `CardDiscard`.

- [ ] **Step 26: Implement `CheckHandSizeSystem`** — Create `src/Game.Systems/CheckHandSizeSystem.cs`. Static class, `Update(World world)`. Queries entities with `CardDiscard`. For each discarded card: determine the deck entity (by matching `CardDeck.OrgId` / `CardDeck.CountryId`). Count current `CardInHand` cards for that deck. If count < `CardHand.HandSize` on the deck entity, add `CardDraw(1)` to the deck entity.

- [ ] **Step 27: Implement `DrawCardSystem`** — Create `src/Game.Systems/DrawCardSystem.cs`. Static class, `Update(World world, ActionConfig config, Random rng)`. Queries `CardDeck` + `CardDraw`. For each: collect candidate card entities for the deck (same `OrgId`+`CountryId`, not carrying `CardInHand`), filter by conditions (for country decks), shuffle via Fisher-Yates, add `CardInHand` to N cards. Remove `CardDraw` from the deck entity.

- [ ] **Step 28: Implement `CleanupCardDiscardSystem`** — Create `src/Game.Systems/CleanupCardDiscardSystem.cs`. Static class, `Update(World world)`. Removes `CardDiscard` from all entities that carry it. This is a trivial one-liner system that runs after `DrawCardSystem` each frame.

- [ ] **Step 29: Migrate opinion storage to resource model** — This is the most cross-cutting change; do it as a discrete step after the pipeline systems are all written.
  - In `src/Game.Systems/InitSystem.cs` (character creation): for each character with opinion data, create `Resource` entities with `ResourceOwner(charId, OwnerType.Character)` and `resourceId = "opinion_{orgId}"` for each org.
  - Remove `CharacterOpinion` and `OpinionModifier` component adds from character creation in `InitSystem` and `GameLogic`.
  - Update `OpinionSystem.Update` to iterate `Resource` entities with `OwnerType.Character` and `resourceId` prefix `"opinion_"`, applying monthly `ResourceEffect` entries (decay). Remove old `CharacterOpinion`-based logic.
  - Update `VisualStateConverter.UpdateCharacters` to read opinion values from `Resource` entities instead of `CharacterOpinion` components.
  - Delete `src/Game.Components/CharacterOpinion.cs` and `src/Game.Components/OpinionModifier.cs`.

- [ ] **Step 30: Wire pipeline into `GameLogic.Update`** — Edit `src/Game.Main/GameLogic.cs`. Replace the `foreach PlayActionCommand` / `foreach PlayCountryActionCommand` blocks and the `ActionSystem.Process*` calls with the ten-step pipeline in order:
  ```
  CleanupActionEffectsSystem.Update(_world);
  InitActionFromPlayCardSystem.Update(_world, _commandAccessor.ReadPlayCardActionCommand());
  CheckActionConditionSystem.Update(_world, _actionConfig);
  DeductActionCostSystem.Update(_world, _actionConfig);
  ActionSucceededSystem.Update(_world, _actionConfig);
  CreateActionEffectSystem.Update(_world, _actionConfig, _effectConfig, currentTime);
  DiscoverCountrySystem.Update(_world, _proximityEntity, _rng);
  RemoveCardFromHandSystem.Update(_world);
  CheckHandSizeSystem.Update(_world);
  DrawCardSystem.Update(_world, _actionConfig, _rng);
  CleanupCardDiscardSystem.Update(_world);
  ```
  Remove `_orgActionResult` / `_countryActionResult` / `lastActionId` local variables. Replace the `VisualState.LastAction.Set(...)` call with `LastFrameEffects` population (see Step 31).

- [ ] **Step 31: Replace `LastAction` with `LastFrameEffects` in `VisualState` and `VisualStateConverter`** — In `src/Game.Main/VisualState.cs`:
  - Add `VisualResourceChangeEffect` class (visual layer only, not ECS): `string EffectId`, `string ResourceId`, `string OwnerId`, `double Amount`.
  - Add `VisualEffectCollection` class with `List<VisualResourceChangeEffect>` and `GetEffectsByActionId(string actionId)` helper.
  - Add `LastFrameEffects` property (`VisualEffectCollection`) to `VisualState`. Implement `INotifyPropertyChanged` so subscribers get notified. Note: `LastFrameEffects` is repopulated each frame from ECS entities and those entities are deleted on the following frame by `CleanupActionEffectsSystem`. Consumers that need effect data for multi-frame work (e.g. animation barriers in `CardPlayAnimator`) must capture all needed data synchronously inside the `PropertyChanged` handler — this is the same established pattern used by the current `LastAction` implementation.
  - Remove `LastActionResultState` class and `LastAction` property from `VisualState`.
  - In `src/Game.Main/VisualStateConverter.cs`: add `UpdateLastFrameEffects(IReadOnlyWorld world)` that iterates `ResourceChange` ECS entities, maps them to `VisualResourceChangeEffect`, populates `_state.LastFrameEffects`. Call it from `Update()`. Remove old `UpdateOrgActions` / `UpdateCountryActions` references to `LastAction`.

- [ ] **Step 32: Migrate `InitSystem` card-entity creation to unified model** — Edit `src/Game.Main/InitSystem.cs` (or wherever card entities are created at startup):
  - For org cards: create entity with `CardInHand`/no tag + `OrgContext(orgId)` + `Action(actionId)` (persistent component, non-execution Action). Also create one `CardDeck` + `CardHand` entity per org.
  - For country cards: create entity with `OrgContext(orgId)` + `CountryContext(countryId)` + `Action(actionId)`. Create `CardDeck(orgId, countryId)` + `CardHand(handSize)` entity per (orgId, countryId) pair.
  - Remove all `ActionCard`, `CountryActionCard`, and `ActionOwner` entity creation. Seed `CardInHand` for the initial hand as before.

- [ ] **Step 33: Update `VisualStateConverter` action views to use unified components** — Edit `UpdateOrgActions` and `UpdateCountryActions` in `src/Game.Main/VisualStateConverter.cs` to query `Action` + `OrgContext` (and `CountryContext`) instead of `ActionCard` / `CountryActionCard`. Query `CardDeck` + `CardHand` for hand-size data. Remove only `IsRateDynamic`, `InfluenceBase`, and `InfluenceBonus` fields from `CountryActionCardEntry` in `VisualState.cs` — keep `SuccessRate` as it is read by `CardPlayAnimator.PlayCountrySequence` during animation. Update `CountryActionsView` and `ActionCardBuilder` to no longer read or render `IsRateDynamic`, `InfluenceBase`, or `InfluenceBonus`.

- [ ] **Step 34: Remove old command types** — Delete `src/Game.Commands/PlayActionCommand.cs` and `src/Game.Commands/PlayCountryActionCommand.cs`. Remove `ReadPlayActionCommand` and `ReadPlayCountryActionCommand` from the command accessor source generator input / generated file.

- [ ] **Step 35: Remove old component types** — Delete:
  - `src/Game.Components/ActionCard.cs`
  - `src/Game.Components/CountryActionCard.cs`
  - `src/Game.Components/InHand.cs` (replaced by `CardInHand`)
  - `src/Game.Components/Effects.cs` (the old `IEffect`, `ResourceChange` class, `CharacterOpinionChange`, `InfluenceAdded`)
  - `src/Game.Components/ActionOwner.cs`

- [ ] **Step 36: Remove `ActionSystem.cs`** — Delete `src/Game.Systems/ActionSystem.cs`. Confirm no remaining references via build.

- [ ] **Step 37: Update Unity UI callsite — `CardPlayAnimator`** — Edit `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`:
  - Replace `_commands.Push(new PlayActionCommand { OwnerId = orgId, ActionId = actionId })` with `_commands.Push(new PlayCardActionCommand { OrgId = orgId, ActionId = actionId })`.
  - Replace `_commands.Push(new PlayCountryActionCommand { ... })` with `_commands.Push(new PlayCardActionCommand { OrgId = orgId, CountryId = countryId, ActionId = actionId })`.
  - Replace `_state.LastAction.PropertyChanged` subscription with `_state.LastFrameEffects.PropertyChanged`.
  - Replace `_state.LastAction.HasResult` / `.Success` / `.Effects` / `.Clear()` accesses with `LastFrameEffects` equivalents.
  - Update `HandleLastActionChanged` to iterate `LastFrameEffects` entries (now `VisualResourceChangeEffect`) instead of `IEffect` subclasses. The animation shows the card with no text; the resource-change effect wiring maps `ResourceChange` (resourceId == "gold") and influence (`ResourceChange` to `influence_{countryId}` resource) for the gold deduction animation. Capture all required effect data (e.g. resource IDs, amounts for barriers) synchronously inside `HandleLastActionChanged` — `LastFrameEffects` will be empty on the next frame.

- [ ] **Step 38: Build and fix compilation errors** — Run `dotnet build src/GlobalStrategy.Core.sln` and resolve any remaining references to deleted types. Rebuild Unity (refresh assets). Check `read_console` for errors.

## User Steps

### 1. Verify in Play mode
After implementation, enter Play mode. Play an org action card and confirm:
- The animation plays correctly (card flies to deck, new card drawn in).
- Gold deducts and the gold animatable transitions.
- "Discovered" overlay fires on success.

Play a country action card and confirm:
- Influence increases (or is capped).
- Opinion changes animate correctly.

### 2. Run test suite
Run `dotnet test src/GlobalStrategy.Core.sln` and confirm all tests pass (or note new failures for follow-up).

## Tests

### Tests to add (`src/Game.Tests/`)

**`UnifiedPipelineTests.cs`** — Integration tests driving the full ten-step pipeline:
- `pipeline_deducts_cost_on_valid_org_action` — push `PlayCardActionCommand` (no `CountryId`), run all 10 systems in order, assert gold deducted.
- `pipeline_does_not_execute_when_insufficient_gold` — assert no `ActionSucceeded` added, gold unchanged.
- `pipeline_card_action_always_succeeds` — assert `ActionSucceeded` present on card entity after DiceRoll step for any valid action.
- `pipeline_discovers_country_on_org_action_success` — assert `IsDiscovered` added to a second country.
- `pipeline_draws_replacement_card_after_play` — assert `CardInHand` count for the deck remains at `CardHand.HandSize` after play.
- `pipeline_country_action_adds_influence_on_success` — push `PlayCardActionCommand` with `CountryId`, assert `InfluenceEffect` created.
- `pipeline_country_action_adds_opinion_resource_on_success` — assert `Resource` entity with `resourceId = "opinion_{orgId}"` created for character owner.
- `cleanup_system_removes_prior_frame_components` — assert `Action`, `ResourceChange`, `DiscoverCountryEffect` absent after cleanup.

**`ResourceEffectMaxTotalTests.cs`** — Unit tests for bounded `ResourceEffect`:
- `resource_effect_stops_applying_when_max_total_reached` — assert no further change to resource value once `|AccumulatedTotal| >= MaxTotal`.
- `resource_effect_applies_normally_when_max_total_zero` — assert unbounded behavior unchanged.

**Migrate and update existing tests:**
- `ActionSystemTests.cs` → rename to `OrgActionPipelineTests.cs`; rewrite helper to use the new pipeline systems rather than `ActionSystem.ProcessPlayAction`.
- `CountryActionSystemTests.cs` → rename to `CountryActionPipelineTests.cs`; rewrite to use pipeline.
- `OpinionSystemTests.cs` → update to use the resource-based opinion model; remove `CharacterOpinion`-based assertions.
- `CharacterVisualStateTests.cs` → update opinion-reading assertions to use `Resource` entities.

## Constitution Check

No conflicts found. All new code lives in `src/` (ECS rule). MonoBehaviours (`CardPlayAnimator`) remain pure presentation glue. No `new` singletons introduced; `IWriteOnlyCommandAccessor` and `VisualState` continue to be resolved through VContainer. One `.asmdef` per feature folder; no new Unity assemblies are created. All code follows tabs, `_` prefix, braces-always style. The plan was written before any implementation (plan-before-implement rule satisfied).

Use /implement to start working on the plan or request changes.
