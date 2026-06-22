# Spec: VisualState and Action System Refactor

## Feature Intent

As a developer, I want the VisualState, action systems, and animation infrastructure to be consistently structured and self-contained, so that adding new actions or animated values requires touching a minimal number of files and the data flow is easy to reason about.

## Acceptance Criteria

### Gold Display — Integer Only

- **Given** the player's gold value **When** it is displayed in the HUD **Then** it is shown as a whole integer with no decimal places (`:F0` format), regardless of the underlying `double` value.

- **Given** `AnimatableDouble` (used for gold) **When** its display value is needed as an integer **Then** `AsInt()` returns `(int)Display` — no callers reimplement this cast themselves.

- **Given** `ResourcesView` displays gold **When** it reads the gold animatable **Then** it calls `.AsInt()` (or reads `Display` cast to int) and has no reference to a separately-passed `AnimatableDouble? playerGoldAnimatable` parameter; the animatable is resolved internally from the state passed to `Refresh`.

---

### Animated Values Embedded in State Entries

- **Given** `CharacterStateEntry` **When** it represents a character's opinion toward the player's org **Then** `Opinion` is an `AnimatableInt` (not a plain `int`), so the animated display value is always collocated with the data entry.

- **Given** `CharactersView` displays a character's opinion **When** it reads opinion **Then** it reads `entry.Opinion.Display` directly; there is no `Dictionary<string, AnimatableInt> characterOpinions` parameter in `CharactersView` or `CountryInfoView`.

- **Given** `CountryInfluenceState` represents the player org's used influence in the selected country **When** influence changes **Then** `UsedInfluence` is an `AnimatableInt`; callers read `UsedInfluence.Display` for the animated display value.

- **Given** `CountryInfoView` displays used influence **When** it renders the influence label **Then** it reads `influence.UsedInfluence.Display`; there is no separate `usedDisplay` parameter in `Refresh` or `RefreshUsedInfluence`.

- **Given** `ResourceStateEntry` holds the value for a resource **When** it is read by a view **Then** `Value` is an `AnimatableDouble`; callers read `entry.Value.Display` (cast to int for gold) for the animated display value.

---

### Animated Value Ticking — No AnimationBarrierDriver

- **Given** `AnimationBarrierDriver` exists **When** refactored **Then** it is deleted entirely; animated values are ticked directly inside `VisualStateConverter.Update(deltaTime)`.

- **Given** `GameLogic.Update(deltaTime)` runs each frame **When** it calls `VisualStateConverter.Update(...)` **Then** it passes `deltaTime` as a parameter; the converter advances all `AnimatableInt` and `AnimatableDouble` instances in the new VisualState structure each frame.

- **Given** a new animated value is introduced anywhere in VisualState **When** it needs to tick **Then** `VisualStateConverter.Update` ticks it alongside the others — no separate driver class is required.

---

### VisualState Property Reorganisation

- **Given** `VisualState` **When** inspected **Then** `PlayerCountry` and `PlayerResources` no longer exist as top-level properties; player-country-specific data is accessed through `PlayerOrganization` (which now contains the player's resource dictionary) or via `SelectedCountry`.

- **Given** `VisualState.PlayerOrganization` **When** inspected **Then** it contains a resource dictionary (`Dictionary<string, AnimatableDouble>`) replacing both the removed `PlayerGold` property and the old `CountryResourcesState`-based approach for org resources; gold is accessed as `PlayerOrganization.Resources["gold"]`. `CountryResourcesState` (with `ResourceStateEntry` entries) is retained only for selected-country resources — it is not used for org resources.

- **Given** `VisualState.SelectedCountry` **When** inspected **Then** it now groups the following sub-states that were previously top-level: `Resources`, `Influence`, `Characters`, `CountryActions`, `UsedInfluence`, `CharacterOpinions` (now embedded in `CharacterStateEntry.Opinion`). The `Selected` prefix is removed from all these property names.

- **Given** `VisualState.PlayerOrganization` **When** inspected **Then** it now groups `Characters` (formerly `PlayerOrgCharacters`) and `Actions` (formerly `PlayerOrgActions`).

- **Given** any view that previously read a removed top-level property (e.g. `_state.SelectedResources`) **When** it is updated **Then** it reads the equivalent property on the new sub-state group (e.g. `_state.SelectedCountry.Resources`); all existing behaviours are preserved.

---

### CardPlayBarriersHolder — Simplified

- **Given** `CardPlayBarriersHolder` **When** it manages animation barriers **Then** it works directly with the animatable values embedded in state entries rather than accepting raw `AnimatableDouble`/`AnimatableInt` references; the internal split between `_doubles` and `_ints` dictionaries is collapsed if a common interface allows it.

- **Given** a card play animation completes **When** barriers are released **Then** the same observable animated transitions occur as before (gold counts down, influence counts up, opinion counts up or down); no regression in animation behaviour.

---

### LastActionResultState — Effect List

- **Given** `LastActionResultState` **When** a card play resolves **Then** it holds `List<IEffect>` instead of specific fields (`GoldSpent`, `InfluenceAdded`, `OpinionTargetCharId`, `OpinionDelta`).

- **Given** `IEffect` **When** implemented **Then** at minimum two concrete types exist:
  - `ResourceChange(ownerId, resourceId, diff)` — represents a resource gain or loss
  - `CharacterOpinionChange(countryId, characterId, diff)` — represents a character opinion delta

- **Given** `CardPlayAnimator` reads `LastActionResultState` **When** it processes a result **Then** it iterates `LastAction.Effects` and dispatches on type to drive animation barriers; no code reads `GoldSpent`, `InfluenceAdded`, `OpinionTargetCharId`, or `OpinionDelta` directly.

- **Given** a new action type is introduced that produces a new kind of effect **When** `LastActionResultState` is used **Then** only a new `IEffect` implementation is needed; `LastActionResultState` itself does not need to change.

---

### Single ActionSystem with Effect Appliers

- **Given** `ActionSystem` and `CountryActionSystem` exist **When** refactored **Then** they are replaced by a single `ActionSystem` that handles both `PlayActionCommand` and `PlayCountryActionCommand`; `CountryActionSystem.cs` is deleted.

- **Given** `ActionSystem` processes any action command **When** it runs **Then** it follows one shared pipeline:
  1. Build `ExpressionContext` from org influence (scoped to `countryId` if provided, global otherwise)
  2. Evaluate `ActionDefinition.Conditions` — return `Executed=false` if any condition is zero
  3. Check and deduct resource cost
  4. Remove the played card from hand (matching on full card identity)
  5. Roll success against `SuccessRateNode`
  6. On success: dispatch each `ActionEffectDefinition` to its static applier by params type
  7. Recompute `ExpressionContext` with updated state
  8. Draw new cards (filtered by conditions if `countryId` is present)

- **Given** effect application **When** the system iterates `actionDef.EffectIds` **Then** it resolves each to an `ActionEffectDefinition` and calls the matching static applier:
  - `DiscoverCountryEffectParams` → `DiscoverCountryApplier.Apply(world, orgId, params, random)`
  - `InfluenceChangeEffectParams` → `InfluenceChangeApplier.Apply(world, orgId, countryId, params)`
  - `OpinionModifierEffectParams` → `OpinionModifierApplier.Apply(world, orgId, countryId, targetCharId, params)`

- **Given** `ActionResult` **When** refactored **Then** it is a single shared type: `bool Executed`, `bool Success`, `List<IEffect> Effects`; the old per-system result types with specific fields (`GoldSpent`, `InfluenceAdded`, `OpinionTargetCharId`, `OpinionDelta`) are deleted.

- **Given** an applier runs **When** it produces an observable game change **Then** it adds the corresponding `IEffect` to the result's `Effects` list:
  - Resource deduction → `ResourceChange(ownerId, resourceId, diff)`
  - Influence gain → `InfluenceAdded(orgId, countryId, amount)`
  - Opinion change → `CharacterOpinionChange(countryId, characterId, diff)`

- **Given** `GameLogic` reads `ActionResult` **When** it populates `LastActionResultState` **Then** it copies `result.Effects` directly; no action-type-specific code in `GameLogic`.

- **Given** a new action effect type is introduced **When** a new `ActionEffectDefinition` subclass is added **Then** only a new static applier class is needed; `ActionSystem`'s pipeline does not change.

---

### VisualStateConverter — Updated Mapping

- **Given** `VisualStateConverter` maps ECS state to VisualState **When** it runs each frame **Then** it writes to the new property paths (e.g. `_state.PlayerOrganization.Resources["gold"].SetActual(...)`) rather than the removed top-level paths.

- **Given** `VisualStateConverter` previously set `_state.PlayerGold.SetActual(...)` **When** refactored **Then** it sets the gold `AnimatableDouble` inside `PlayerOrganization.Resources` instead.

---

## Out of Scope

- Changes to card configs, action definitions, or balance values.
- New action types beyond what already exists.
- New animated values beyond the three already animating (gold, used influence, character opinion).
- Saving/loading of animated value mid-animation state.
- Visual changes to how values are displayed (styling, colour, transitions) — only format changes mandated by the integer-display rule.
- Localization key changes.
- Any changes to the ECS components or game logic outside `ActionSystem`, the new applier classes, and `GameLogic`'s result-handling path.
- Adding new effect types beyond the three that exist today.
