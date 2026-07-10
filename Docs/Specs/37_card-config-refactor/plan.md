# Plan: Card Config Refactor

## Spec

A single unified `action_config.json` replaces both the existing `action_config.json` and `country_action_config.json`. All action definitions share a common schema with an `ownerType` discriminator (`"org"` or `"country"`). Country-specific fields (`targetRole`, `deckCopies`, `cooldownDays`) and org-specific fields (`effectIds`) coexist in the same `ActionDefinition` class. The `preDealtToHand` flag is removed; initial hand population for country actions is derived from evaluating each card's `conditions` expression against current game state. `prices`/`goldCost` are replaced by a unified `cost` array. `controlThreshold` is replaced by a DSL-expression `conditions` array. `cooldownMonths` is replaced by `cooldownDays`. Flat `successRateBase`/`successRateControlDivisor` fields are replaced by a recursive `successRate` expression tree using a new `ExpressionNode` class supporting `value`, `add`, `sub`, `mul`, `div`, `clamp`, `control`, `opinion`, `gte`, `lte`, `gt`, `lt`, `eq` operations. Outcome fields (`controlOnSuccess`, `opinionModifierSourceId/Value/ChangeValue`, `minCountryChance`) move to typed effect subclasses (`ControlChangeEffectParams`, `OpinionModifierEffectParams`, `DiscoverCountryEffectParams`) in `effect_config.json`. `CountryActionConfig.cs` and `CountryActionDefinition` are deleted. Visual config stays in the existing `ActionVisualConfig` ScriptableObject with no changes. Character defaults are removed from the `defaults` array.

## Goal

Replace the two separate action config files and their C# models with a single unified schema and a composable DSL expression tree for success rates and conditions, moving all outcome parameters into typed effect definitions, so that balance and effect changes can be made in JSON without touching C# models.

## Approach

The refactor proceeds in three layers from the inside out: first the pure-C# `src/` domain models (`ExpressionNode`, typed effect subclasses, unified `ActionDefinition`), then the game-logic consumers (`InitSystem`, `CountryActionSystem`, `ActionSystem`, `VisualStateConverter`, `GameLogic`, `GameLogicContext`), and finally the JSON config files and Unity-side wiring. Tests are updated in lockstep with the `src/` changes. The Unity side (`GameLifetimeScope`, UI documents) is updated last to drop all references to the deleted `CountryActionConfig` type.

## Agent Steps

- [x] **Add `ExpressionNode` to `Game.Configs`** — Create `src/Game.Configs/ExpressionNode.cs` with the `ExpressionNode` class. It must have `string Type`, `double Value`, and `List<ExpressionNode> Members`. `Value` stores a literal number for `"value"` nodes. `Members` holds only child `ExpressionNode` objects for compound ops (`add`, `div`, etc.). Include one static `Evaluate(ExpressionNode node, ExpressionContext ctx)` method (returns `double`). `ExpressionContext` is a companion struct/class in the same file carrying `double Control` and `double Opinion` fields — enough for this iteration. Supported op evaluation: `value` → return `node.Value` directly; `add` → sum of all member evaluations; `sub` → `Members[0] - Members[1]`; `mul` → product of all; `div` → `Members[0] / Members[1]` (return 0 when denominator is 0); `clamp` → clamp `Members[0]` between `Members[1]` and `Members[2]`; `control` → `ctx.Control`; `opinion` → `ctx.Opinion`; boolean ops (`gte`, `lte`, `gt`, `lt`, `eq`) → evaluate both members and compare, return 1.0 for true, 0.0 for false. There is no `"__literal"` node type and no custom converter is needed for value nodes — `"value"` nodes simply carry their literal in `Value` and have an empty `Members` list.

- [x] **Add typed effect subclasses to `EffectConfig.cs`** — Add three subclasses inside `src/Game.Configs/EffectConfig.cs`: `DiscoverCountryEffectParams` (adds `double MinCountryChance`), `ControlChangeEffectParams` (adds `int Amount`), `OpinionModifierEffectParams` (adds `string SourceId`, `int InitialValue`, `int DecayPerMonth`). All extend `ActionEffectDefinition`. Add a Newtonsoft `JsonConverter` for `ActionEffectDefinition` that reads the `effectType` field from the JSON object, then deserializes the full object to the correct subclass. Register it on `EffectConfig.Effects` via `[JsonConverter]` attribute or inside the `EffectConfig` class's property declaration.

- [x] **Update `ActionDefinition` in `ActionConfig.cs`** — In `src/Game.Configs/ActionConfig.cs`: replace `List<ActionCondition> Conditions` with `List<ExpressionNode> Conditions` (same property name, type changed); delete the `ActionCondition` class. Remove `ActionPrice`, `float SuccessRate`, `float MinCountryChance`, and `List<ActionPrice> Prices`. Add: `string OwnerType`, `string TargetRole`, `int DeckCopies`, `int CooldownDays`, `List<ActionCost> Cost`, `ExpressionNode? SuccessRateNode`. Add `ActionCost` class (`string ResourceId`, `double Amount`) in the same file. Remove the `character` entry from `ActionOwnerDefaults` — the class itself stays but the JSON data will no longer carry a character entry. Keep `ActionConfig`, `ActionOwnerDefaults`, `OrgActionPool`, and the `Find`/`GetHandSize`/`GetOrgPool` helper methods unchanged. Keep `List<string> EffectIds` on `ActionDefinition`.

- [x] **Delete `CountryActionConfig.cs`** — Remove `src/Game.Configs/CountryActionConfig.cs` and its `.meta` file (if any). Verify the file is gone.

- [x] **Update `ActionSystem.cs`** — In `src/Game.Systems/ActionSystem.cs`: replace `CanAfford`/`DeductPrices` to use `ActionDefinition.Cost` (`List<ActionCost>`) instead of `ActionDefinition.Prices` (`List<ActionPrice>`). Replace the flat `actionDef.SuccessRate` success roll with `(float)ExpressionNode.Evaluate(actionDef.SuccessRateNode, ctx)` where `ctx = new ExpressionContext { Control = orgControl }`. Look up org control before the roll (query `ControlEffect` components for the cmd's org). In `ApplyDiscoverCountry`, find the `DiscoverCountry` effect by looking up each `effectId` in the passed `EffectConfig`, casting the result to `DiscoverCountryEffectParams`, and reading `MinCountryChance` from it; keep the proximity-weighting logic identical. Update the `ProcessPlayAction` signature to also accept `EffectConfig effectConfig` and pass it through to `ApplyDiscoverCountry`.

- [x] **Update `CountryActionSystem.cs`** — In `src/Game.Systems/CountryActionSystem.cs`: change `ProcessPlayCountryAction` to accept `ActionConfig config` (not `CountryActionConfig`). Use `config.Find(cmd.ActionId)` to get an `ActionDefinition`. Replace the `ControlThreshold` eligibility check with evaluating all `Conditions` expressions: build an `ExpressionContext { Control = orgControl }` and evaluate each node; reject the action if any returns 0. Replace the gold-cost check and deduction with the generic `Cost`-list approach (same logic as updated `ActionSystem.CanAfford`/`DeductPrices`). Replace `def.CooldownMonths` with `def.CooldownDays`: compute `cooldownEnd = currentTime.AddDays(def.CooldownDays)` (no cooldown when `CooldownDays == 0`). Replace the flat success-rate computation with `ExpressionNode.Evaluate(def.SuccessRateNode, ctx)`. Replace the inline control-on-success block with: look up each `effectId` in an `EffectConfig effectConfig` parameter; if the resulting `ActionEffectDefinition` is an `ControlChangeEffectParams`, apply control delta `Amount`; if it is an `OpinionModifierEffectParams`, guard with `if (!string.IsNullOrEmpty(params.SourceId))` before applying the opinion modifier using `SourceId`, `InitialValue`, `DecayPerMonth`. Update the draw-eligible-cards filter to evaluate `Conditions` instead of comparing `ControlThreshold`. Update the `TickCooldowns` method signature — no change needed, it only touches ECS and has no dependency on config types. Add `EffectConfig effectConfig` as a parameter to `ProcessPlayCountryAction`.

- [x] **Update `GameLogicContext.cs`** — Remove `IConfigSource<CountryActionConfig> CountryAction` property and the `countryAction` constructor parameter. Remove the `EmptyCountryActionConfig` nested class. Add `IConfigSource<EffectConfig> Effect` if not already present (it already exists — verify it is passed through). No other changes needed.

- [x] **Update `GameLogic.cs`** — Remove `CountryActionConfig _countryActionConfig`, `CountryActionConfig CountryActionConfig` public property, and the `context.CountryAction.Load()` call. Update the `VisualStateConverter` constructor call to pass `ActionConfig` instead of `CountryActionConfig`. Add `EffectConfig _effectConfig` field, assigned in the constructor via `_effectConfig = context.Effect.Load()`. Expose `public EffectConfig EffectConfig { get; }` as a public property on `GameLogic`, assigned in the constructor. In `Update()`, update both call sites: `ActionSystem.ProcessPlayAction(_world, cmd, _actionConfig, _effectConfig, _proximityEntity, _rng)` and `CountryActionSystem.ProcessPlayCountryAction(_world, cmd, _actionConfig, _effectConfig, currentTime, _rng)`.

- [x] **Update `VisualStateConverter.cs`** — Replace the `CountryActionConfig? _countryActionConfig` field with `ActionConfig? _actionConfig`. Update the constructor parameter. In `UpdateCountryActions`, call `_actionConfig?.Find(card.ActionId)` returning an `ActionDefinition`. In `BuildEntry`, replace flat success-rate computation with `ExpressionNode.Evaluate(def.SuccessRateNode, ctx)`. Replace `def.ControlThreshold` eligibility check with evaluating `def.Conditions`. Replace `isDynamic` flag with a heuristic: the expression tree is dynamic if `SuccessRateNode` is not a plain `value` node (check `def.SuccessRateNode?.Type != "value"`). Replace `controlBase`/`controlBonus` computation: `controlBase` = evaluate `SuccessRateNode` with `Control = 0` × 100; `controlBonus` = (evaluate with actual control × 100) − `controlBase`. Replace `def.ControlThreshold` in the `insufficientControl` check with: all conditions pass with current control? (evaluate each condition node; `insufficientControl = true` if any returns 0). Keep the `poolFull` check for `sphere_of_pressure` as-is until a more general mechanism is designed (it is a known special-case).

- [x] **Update `InitSystem.cs`** — In `CreateCountryActionEntities`, change the source from `context.CountryAction.Load()` to `context.Action.Load()` and filter by `def.OwnerType == "country"`. Replace `def.PreDealtToHand` initial hand deal with: after all card entities are created for a given org+country, evaluate each card's `def.Conditions` against `ExpressionContext { Control = 0 }`; collect all eligible card entities; shuffle them; assign `InHand { SlotIndex = i }` to fill up to the country hand size (read from `actionConfig.GetHandSize("country")`). No slot-0 pinning, no `copyIndex` guard — all copies of any eligible card can enter the hand, duplicates allowed.

- [x] **Update `ActionSystemTests.cs`** — In `BuildActionConfig`, replace `Prices` with `Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 100 } }`. Replace flat `SuccessRate = successRate` with `SuccessRateNode = new ExpressionNode { Type = "value", Value = successRate }`. Remove `MinCountryChance` from `ActionDefinition` construction. Update `ProcessPlayAction` call signatures to pass an `EffectConfig` containing a `DiscoverCountryEffectParams` with `MinCountryChance = 0.01f`. Ensure all existing test scenarios still pass.

- [x] **Rewrite `CountryActionSystemTests.cs`** — Replace `CountryActionConfig`/`CountryActionDefinition` usage with `ActionConfig`/`ActionDefinition`. Replace `BuildConfig` helper to construct `ActionDefinition` with `OwnerType = "country"`, `SuccessRateNode` expression tree, `Conditions` list (convert `controlThreshold` to a `gte` node comparing `control` to a `value` node), `CooldownDays` (convert months × 30), `Cost` list (convert `goldCost`), and `EffectIds` pointing to effect entries in a companion `EffectConfig`. Replace `BuildWorld` gold entity to use the generic `Cost`-based resource approach. Pass `ActionConfig` and `EffectConfig` to `ProcessPlayCountryAction`. Update cooldown assertions to use `AddDays` instead of `AddMonths`. All existing test scenarios must be preserved and must still pass.

- [x] **Rewrite `action_config.json`** — Replace `Assets/Configs/action_config.json` with the unified schema: remove `character` from `defaults`; move all 6 country actions from `country_action_config.json` into the `actions` array with `ownerType: "country"`, converting `cooldownMonths` → `cooldownDays` (×30), removing `goldCost`/`prices` (cost is empty array for all actions in this iteration — gold is removed from the economy), converting `controlThreshold` → a `conditions` entry using a `gte` / `control` / `value` DSL expression, converting flat success-rate fields → `successRate` expression tree, adding `effectIds` referencing the new effect entries. Keep the existing `discover_country` org action; update it to use `successRate` expression tree and remove `prices`/`minCountryChance`. Remove `orgPools` `character` entries if any.

- [x] **Rewrite `effect_config.json`** — Replace `Assets/Configs/effect_config.json` with entries for all effect types referenced from the new `action_config.json`: `discover_country` (type `DiscoverCountry`, `minCountryChance: 0.01`); `sphere_of_pressure_control` (type `ControlChange`, `amount: 10`); `letter_commendation_control` (type `ControlChange`, `amount: 1`); `letter_commendation_opinion` (type `OpinionModifier`, `sourceId: "letter_of_commendation"`, `initialValue: 50`, `decayPerMonth: 1`); `royal_audience_control` (type `ControlChange`, `amount: 2`); `royal_audience_opinion` (type `OpinionModifier`, `sourceId: "royal_audience"`, `initialValue: 25`, `decayPerMonth: 1`). Remove `sphere_of_pressure_opinion` — it has an empty `sourceId` and zero values and produces no effect. Include `nameKey`/`descKey` for each (match locale keys already in `en.asset`/`ru.asset` or use placeholder keys).

- [x] **Delete `country_action_config.json`** — Remove `Assets/Configs/country_action_config.json` and its `.meta` file.

- [x] **Update `GameLifetimeScope.cs`** — Remove the `_countryActionConfigAsset` serialized field and the `countryAction:` argument in the `GameLogicContext` constructor call. Remove the `builder.Register(c => c.Resolve<GameLogic>().CountryActionConfig, ...)` line. Add `builder.Register(c => c.Resolve<GameLogic>().EffectConfig, Lifetime.Singleton);`. Verify that `ActionConfig` is still correctly registered.

- [x] **Update Unity UI files** — Update the following four classes in the stated order to replace `CountryActionConfig` with `ActionConfig` and add `EffectConfig` injection where needed:
  1. `HUDDocument.Construct`: change parameter `CountryActionConfig countryActionConfig` → `ActionConfig actionConfig`.
  2. `CountryInfoView` constructor: change parameter type `CountryActionConfig` → `ActionConfig`, pass through.
  3. `CountryActionsView` constructor and `_config` field: change type `CountryActionConfig` → `ActionConfig`.
  4. `CardPlayAnimator.Construct`: remove `CountryActionConfig`, change `_countryActionConfig` field type to `ActionConfig _actionConfig`; also inject `EffectConfig _effectConfig` (needed for `OpinionModifierEffectParams` lookup).

  Apply the following explicit field replacements in `CountryActionsView`, `CardTransitionView`, and `CardPlayAnimator`:
  - `def.GoldCost` → find the first `ActionCost` in `def.Cost` where `ResourceId == "gold"`, use `Amount` (or 0 if absent).
  - `def.SuccessRateBase * 100` (country action path) → `(int)(ExpressionNode.Evaluate(def.SuccessRateNode, new ExpressionContext()) * 100)`.
  - `def.SuccessRate * 100` (org action path in `CardTransitionView` and `CardPlayAnimator`) → `(int)(ExpressionNode.Evaluate(def.SuccessRateNode, new ExpressionContext()) * 100)`.
  - `def.Prices` → `def.Cost` (same `{ ResourceId, Amount }` shape, type renamed from `ActionPrice` to `ActionCost`).
  - `def.OpinionModifierSourceId` check → iterate `def.EffectIds`, look each up in the injected `EffectConfig`, cast to `OpinionModifierEffectParams`, use a non-empty `SourceId` as the guard.

  Ensure injected `ActionConfig` and `EffectConfig` are already registered in `GameLifetimeScope`.

- [x] **Rebuild the DLL** — Run `dotnet build src/GlobalStrategy.Core.sln -c Release` and confirm it succeeds with no errors. Then refresh Unity and check the console for compilation errors.

## User Steps

### 1. Inspect the Inspector for removed serialized field

Open `Assets/Scenes/Game/Game.unity` (or whichever scene contains `GameLifetimeScope`) in the Unity Editor. The `_countryActionConfigAsset` field that was removed from `GameLifetimeScope.cs` will show as a missing reference in the Inspector. Select the `GameLifetimeScope` GameObject and confirm the stale field is gone after domain reload. No reassignment is needed — the field is deleted, not renamed.

### 2. Verify no stale `CountryActionConfig` asset reference warnings

After domain reload, check the Unity Console for any "The type or namespace name 'CountryActionConfig'" errors. If any appear, a file in `Assets/Scripts/` was missed in the Unity UI update step above — find it via the error and remove the reference.

### 3. Verify the action card UI in Play mode

Enter Play mode with the Game scene. Select a country that has country action cards. Confirm:
- Cards that were previously `preDealtToHand: true` (i.e. `sphere_of_pressure` with control threshold 0) appear in the opening hand.
- Cards with an control condition above 0 do not appear in the opening hand.
- Cooldowns display correctly (in days, not months).
- Success-rate tooltips show a plausible value for cards with expression-based rates.

## Tests

The following test scenarios must be explicitly covered in `src/Game.Tests/`:

**`ActionSystemTests.cs` (updated):**
- `play_action_deducts_cost_resource` — playing an action deducts the `Cost` resource, not a hard-coded gold field.
- `play_action_insufficient_cost_returns_not_executed` — action is blocked when the org has less than the cost amount.
- `play_action_success_discover_country_uses_min_chance_from_effect` — `DiscoverCountryEffectParams.MinCountryChance` is used in weight flooring, not a field on `ActionDefinition`.
- `play_action_success_rate_from_expression_node` — when `SuccessRateNode` is `new ExpressionNode { Type = "value", Value = 0.75 }`, the success probability is 0.75 (existing test updated in place).

**`CountryActionSystemTests.cs` (rewritten):**
- `play_returns_not_executed_if_insufficient_cost_resource` — replaces the gold-specific test; uses generic `Cost` array.
- `play_deducts_cost_resource_on_execution` — deduction uses the `Cost` list.
- `play_returns_not_executed_when_conditions_not_met` — a `gte` condition node comparing `control` to `value(10)` blocks play when control is 0.
- `play_allowed_when_conditions_met` — same condition passes when control is 10.
- `play_success_applies_control_change_from_effect` — `ControlChangeEffectParams.Amount` drives the control delta.
- `play_success_does_not_apply_control_when_pool_full` — cap-at-100 logic is preserved.
- `play_success_capped_at_pool_limit` — partial add when pool is nearly full.
- `play_failure_does_not_apply_control_or_opinion` — on failure, no effect subclasses are applied.
- `play_assigns_cooldown_in_days` — cooldown end time is `currentTime.AddDays(cooldownDays)`, not `AddMonths`.
- `tick_removes_expired_cooldown` — unchanged semantics, updated setup.
- `tick_keeps_active_cooldown` — unchanged semantics, updated setup.
- `play_success_applies_opinion_modifier_from_effect` — `OpinionModifierEffectParams` fields (`SourceId`, `InitialValue`, `DecayPerMonth`) are used; `SourceId` is non-empty so the guard passes.
- `play_failure_does_not_add_opinion_modifier` — no modifier on failure.
- `cooldown_cards_not_drawn_to_hand` — unchanged semantics.
- `play_draws_from_eligible_deck_card` — unchanged semantics; eligible check uses expression evaluation instead of `ControlThreshold`.

**`ExpressionNodeTests.cs` (new file):**
- `value_node_returns_literal` — `new ExpressionNode { Type = "value", Value = 0.5 }` evaluates to 0.5.
- `add_node_sums_members` — `add(value(0.3), value(0.2))` evaluates to 0.5.
- `div_node_returns_quotient` — `div(value(10), value(2))` evaluates to 5.0.
- `div_node_zero_denominator_returns_zero` — `div(value(1), value(0))` evaluates to 0.
- `control_node_returns_context_control` — `{ type: "control" }` with `ctx.Control = 15` evaluates to 15.
- `gte_node_true_returns_one` — `gte(control, value(10))` with `ctx.Control = 10` returns 1.0.
- `gte_node_false_returns_zero` — same with `ctx.Control = 9` returns 0.0.
- `composite_success_rate` — `add(value(0.3), div(control, value(2)))` with `ctx.Control = 4` evaluates to 0.5.
- `clamp_node` — `clamp(add(value(0.3), div(control, value(2))), value(0), value(1))` with high control clamps to 1.

## Constitution Check

No conflicts found — plan aligns with all principles.

- ECS for all game logic: `ExpressionNode.Evaluate` and all effect application remain in `src/` (pure C#). MonoBehaviours are updated only to drop removed types and pass updated dependencies.
- VContainer is the sole DI mechanism: `GameLifetimeScope` continues to register all configs; no new singletons bypass VContainer.
- UI Toolkit only: no UI component type changes.
- One `.asmdef` per feature folder: no new assemblies are introduced; all new files go into existing `Game.Configs` and `Game.Tests` assemblies.
- Tabs for indentation, `_` prefix for private fields, braces always: to be enforced during implementation.

Use /implement to start working on the plan or request changes.
