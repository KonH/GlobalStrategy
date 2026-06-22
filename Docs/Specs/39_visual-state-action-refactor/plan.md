# Plan: VisualState and Action System Refactor

## Spec

Restructure `VisualState`, `ActionSystem`/`CountryActionSystem`, and the animation infrastructure so that: animated values live inside their owning state entries (not in parallel top-level dictionaries/properties on `VisualState`); `VisualStateConverter` ticks all animatables directly (removing `AnimationBarrierDriver`); the two action systems merge into one with a shared `List<IEffect>` result; `LastActionResultState` carries an effect list instead of named fields; `VisualState` properties are reorganised so `PlayerOrganization` owns characters/actions/resources and `SelectedCountry` owns resources/influence/characters/actions; and gold is displayed as an integer everywhere.

## Goal

Eliminate parallel data structures, make animated values self-contained inside their owning state objects, and unify the two action systems so that adding a new animated value or action effect requires touching the minimum number of files.

## Approach

The refactor has four largely independent pillars, done in order to avoid long compile-broken windows:

1. **Animatable helpers & gold display** — add `AsInt()` to `AnimatableDouble`, change gold label formatting to `F0`, and remove the `_playerGoldAnimatable` parameter from `ResourcesView`/`PlayerOrgView`.
2. **Embed animatables in state entries** — move `AnimatableInt Opinion` into `CharacterStateEntry`, `AnimatableInt UsedInfluence` into `CountryInfluenceState`, `AnimatableDouble Value` into `ResourceStateEntry`; update `VisualStateConverter` to set actuals on the embedded animatables; update all view callsites to read from the entry directly.
3. **VisualState reorganisation** — dissolve `PlayerCountry`, `PlayerResources`, `PlayerGold`, `SelectedResources`, `SelectedInfluence`, `SelectedCharacters`, `SelectedCountryActions`, `SelectedCountryUsedInfluence`, `CharacterOpinions`, `PlayerOrgCharacters`, `PlayerOrgActions` as top-level `VisualState` properties; `PlayerOrganization` gains `Resources`, `Characters`, `Actions`; `SelectedCountry` gains `Resources`, `Influence`, `Characters`, `CountryActions`; `VisualStateConverter` writes to the new paths; all Unity-side binding code is updated.
4. **Unified ActionSystem + IEffect** — define `IEffect` and three concrete types in `src/Game.Main`; rewrite `ActionSystem` to produce `List<IEffect>` results; delete `CountryActionSystem`; update `GameLogic` to pass both command types through the single system; update `LastActionResultState` to hold `List<IEffect>`; update `CardPlayAnimator` to dispatch by type; remove `AnimationBarrierDriver` and move its ticking into `VisualStateConverter.Update(deltaTime)`.

Pillars 1–2 are safe to implement atomically. Pillar 3 is a large rename/restructure and should be done as a single commit to keep the diff coherent. Pillar 4 involves `src/` changes and must be followed by a `dotnet build` and a Unity compile check before moving on.

## Agent Steps

- [ ] **Add `AsInt()` to `AnimatableDouble`** — add `public int AsInt() => (int)Display;` in `src/Game.Main/AnimatableDouble.cs`.

- [ ] **Change gold format to integer** — in `ResourcesView.Refresh` (Unity side), change `$"{displayValue:F0}"` to use `AsInt()` if using the animatable, otherwise `(int)resource.Value`; update `ResourcesView` to not accept `AnimatableDouble? playerGoldAnimatable` parameter (remove field and constructor parameter); update `PlayerOrgView` constructor accordingly; update `HUDDocument.Start` where `PlayerOrgView` and `CountryInfoView` are constructed (drop the `_state?.PlayerGold` argument).

- [ ] **Embed `AnimatableInt Opinion` into `CharacterStateEntry`** — change `CharacterStateEntry.Opinion` from `int` to `AnimatableInt` in `src/Game.Main/VisualState.cs`; update the constructor to accept an `AnimatableInt` instead of `int`; in `VisualStateConverter.UpdateCharacters`, create/reuse `AnimatableInt` instances keyed on `charId` in a `Dictionary<string, AnimatableInt> _characterOpinionAnimatables` field on the converter; call `SetActual(effective)` on the reused instance; pass it to `CharacterStateEntry`; expose `_characterOpinionAnimatables` as an internal property (`internal IReadOnlyDictionary<string, AnimatableInt> CharacterOpinionAnimatables`) so `CardPlayAnimator` can look up the stable animatable by `charId` — never via the entry list (which is rebuilt each frame); remove the `_state.CharacterOpinions` dictionary usage in converter; in `CharactersView.BuildCharacterCard` read `entry.Opinion.Display` directly (remove `_characterOpinions` parameter and field); update `CountryInfoView` constructor and `CharactersView` construction to not pass `characterOpinions`.

- [ ] **Embed `AnimatableInt UsedInfluence` into `CountryInfluenceState`** — change `CountryInfluenceState.UsedInfluence` from `int` to `AnimatableInt` in `VisualState.cs`; update `Set(int used, ...)` to call `UsedInfluence.SetActual(used)`; in `VisualStateConverter.UpdateSelectedInfluence` remove the separate `_state.SelectedCountryUsedInfluence.SetActual(...)` call (now done via the embedded value); update `CountryInfoView.RefreshInfluence` to read `influence.UsedInfluence.Display`; remove `usedDisplay` parameter from `CountryInfoView.Refresh` and `RefreshUsedInfluence`; update `HUDDocument` to remove `HandleAnimatedInfluenceChanged` subscription and the `(int)_state.SelectedCountryUsedInfluence.Display` argument in `RefreshCountryViews`.

- [ ] **Embed `AnimatableDouble Value` into `ResourceStateEntry`** — change `ResourceStateEntry.Value` from `double` to `AnimatableDouble` in `src/Game.Main/ResourcesState.cs`; update the constructor; add a `Dictionary<(string countryId, string resourceId), AnimatableDouble> _resourceAnimatables` field to `VisualStateConverter`; update `BuildResources` signature to thread through this dictionary; call `Value.SetActual(resources[i].Value)` on a reused-or-new `AnimatableDouble` keyed on `(countryId, resourceId)`; update `ResourcesView.Refresh` to read `resource.Value.Display` (or `resource.Value.AsInt()` for gold).

- [ ] **Move animatable ticking to `VisualStateConverter`** — add `deltaTime` parameter to the existing `Update` method; at the end of `Update`, tick all known animatables: iterate `_characterOpinionAnimatables` dictionary, `_resourceAnimatables` dictionary, and `SelectedCountry.Influence.UsedInfluence`, calling `.Tick(deltaTime)` on each; update `GameLogic.Update` to pass `deltaTime` to `_visualStateConverter.Update`; also update `HUDDocument.AnimateGoldDebug` (the debug `+1000` gold button) and any `PushChangeGoldCommand` handler to read the gold `AnimatableDouble` from `_state.PlayerOrganization.Resources["gold"]` instead of the removed `_state.PlayerGold`.

- [ ] **Delete `AnimationBarrierDriver`** — remove `Assets/Scripts/Unity/UI/AnimationBarrierDriver.cs` and its `.meta`; remove `builder.RegisterEntryPoint<AnimationBarrierDriver>()` from `GameLifetimeScope`; remove `VisualState.CharacterOpinions`, `VisualState.PlayerGold`, `VisualState.SelectedCountryUsedInfluence` (the old top-level animated properties) after confirming no remaining references.

- [ ] **Reorganise `VisualState` — PlayerOrganization gets Resources, Characters, Actions** — in `PlayerOrganizationState`: add `CountryResourcesState Resources { get; } = new()`, `OrgCharactersState Characters { get; } = new()`, `OrgActionsState Actions { get; } = new()`; update `VisualStateConverter` to write to these (`UpdateResources` writes player resources to `_state.PlayerOrganization.Resources`; `UpdateOrgCharacters` writes to `_state.PlayerOrganization.Characters`; `UpdateOrgActions` writes to `_state.PlayerOrganization.Actions`); remove `VisualState.PlayerResources`, `VisualState.PlayerOrgCharacters`, `VisualState.PlayerOrgActions` top-level properties.

- [ ] **Reorganise `VisualState` — SelectedCountry gets Resources, Influence, Characters, CountryActions** — add to `SelectedCountryState`: `CountryResourcesState Resources { get; } = new()`, `CountryInfluenceState Influence { get; } = new()`, `CountryCharactersState Characters { get; } = new()`, `CountryActionsState CountryActions { get; } = new()`; update `VisualStateConverter` to write to `_state.SelectedCountry.Resources`, `.Influence`, `.Characters`, `.CountryActions`; remove `VisualState.SelectedResources`, `VisualState.SelectedInfluence`, `VisualState.SelectedCharacters`, `VisualState.SelectedCountryActions` top-level properties.

- [ ] **Reorganise `VisualState` — remove PlayerCountry** — remove `VisualState.PlayerCountry` and `PlayerCountryState` class; remove `VisualStateConverter.UpdatePlayerCountry`; in `HUDDocument`, enumerate every lambda that passes a `PlayerCountryState` argument (including character debug panel button callbacks) and rewrite them to source the player country ID from `_state.PlayerOrganization`; update `CountryInfoView.Refresh` to remove the `PlayerCountryState player` parameter and all three callsites in `HUDDocument` that pass it.

- [ ] **Update all Unity-side binding code after property reorganisation** — `HUDDocument`: remap `PropertyChanged` subscriptions from removed flat properties to new nested ones (`_state.PlayerOrganization.Resources.PropertyChanged`, `_state.SelectedCountry.Resources.PropertyChanged`, etc.); update all `Refresh` call arguments (`_state.SelectedCountry.Resources` instead of `_state.SelectedResources`, etc.); for influence animation, subscribe to `_state.SelectedCountry.Influence.UsedInfluence.PropertyChanged` (the `AnimatableInt`'s own per-tick event) rather than removing the subscription entirely — this keeps the counter animating between game ticks; `OrgInfoDocument`: add a dedicated step — update all references from `_state.PlayerResources`, `_state.PlayerOrgCharacters`, `_state.PlayerOrgActions` to `_state.PlayerOrganization.Resources`, `.Characters`, `.Actions`; `OrgLensCountryView`: update if it reads old influence path.

- [ ] **Define `IEffect` and implementations in `src/Game.Components`** — create `src/Game.Components/Effects.cs` (not `Game.Main`) with: `public interface IEffect { }`, `public class ResourceChange : IEffect { public string OwnerId; public string ResourceId; public double Diff; }`, `public class CharacterOpinionChange : IEffect { public string CountryId; public string CharacterId; public int Diff; }`, `public class InfluenceAdded : IEffect { public string OrgId; public string CountryId; public int Amount; }`. This avoids a circular project reference: `Game.Systems` cannot reference `Game.Main` (Game.Main already depends on Game.Systems); placing `IEffect` in `Game.Components` — which both assemblies already reference — keeps the dependency graph acyclic.

- [ ] **Update `LastActionResultState` to hold `List<IEffect>`** — replace `GoldSpent`, `InfluenceAdded`, `OpinionTargetCharId`, `OpinionDelta` fields with `List<IEffect> Effects { get; private set; } = new()`; update `Set(bool success, string actionId, List<IEffect> effects)`; update `Clear()`.

- [ ] **Merge `CountryActionSystem` into `ActionSystem` with `List<IEffect>` result** — update `ActionSystem.ActionResult` to contain `bool Executed`, `bool Success`, `List<IEffect> Effects`; add three internal static applier methods: `ApplyDiscoverCountry` (existing logic, no `IEffect` emitted since no tracked numeric delta), `ApplyInfluenceChange` (emits `InfluenceAdded`), `ApplyOpinionModifier` (emits `CharacterOpinionChange`); add `ProcessPlayCountryAction` method (currently in `CountryActionSystem`) with the shared cost-check/deduct/card-remove/roll pattern, calling the applier methods; extract shared helpers (`CanAfford`, `DeductPrices`) so both entry points reuse them; update `ActionSystem.ProcessPlayAction` to emit `ResourceChange` for gold spent; delete `src/Game.Systems/CountryActionSystem.cs`.

- [ ] **Update `GameLogic.Update` to use unified ActionSystem** — remove `CountryActionSystem.ActionResult countryActionResult`; call `ActionSystem.ProcessPlayCountryAction` for `PlayCountryActionCommand`; collect effects from both results into one list; pass merged `List<IEffect>` to `VisualState.LastAction.Set`; remove `currentTime` argument threading (now passed inside `ProcessPlayCountryAction`).

- [ ] **Update `CardPlayAnimator` barrier creation to dispatch by IEffect type** — in `HandleLastActionChanged`, iterate `_state.LastAction.Effects` and for each: `ResourceChange` with `resourceId == "gold"` → call `_barrierHolder.AddDouble("gold", ...)` using the `AnimatableDouble` from `_state.PlayerOrganization.Resources`; `InfluenceAdded` → call `_barrierHolder.AddInt("influence", ...)` using `_state.SelectedCountry.Influence.UsedInfluence`; `CharacterOpinionChange` → call `_barrierHolder.AddInt("opinion", ...)` using the opinion animatable from the matching `CharacterStateEntry`; update `PlaySequence` and `PlayCountrySequence` barrier-release logic accordingly.

- [ ] **Build `src/` and fix any compilation errors** — run `dotnet build src/GlobalStrategy.Core.sln -c Release` and resolve errors; then `refresh_unity` and `read_console(types=["error"])` to confirm Unity compilation is clean.

- [ ] **Remove now-unreachable dead code** — delete `src/Game.Main/DictionaryExtensions.cs` (only needed for `CharacterOpinions.GetOrCreate` which is gone); audit for any remaining references to removed classes/properties.

- [ ] **Run tests** — run `dotnet test src/GlobalStrategy.Core.sln` and fix any broken tests; add new tests (see Tests section).

## User Steps

### 1. Verify animated values in Unity Editor

After the build succeeds, enter Play mode and confirm:
- Gold in the player org panel shows as integer (no decimals).
- Playing an org action still animates the gold counter (barrier holds, then counts down).
- Playing a country action still animates influence and opinion values.
- Selecting a new country resets characters and influence display correctly.

### 2. Confirm debug gold animation still works

Click the `+1000` gold debug button in the debug panel and verify the gold counter animates up (the barrier-then-release pattern in `HUDDocument.AnimateGoldDebug` must be updated to read the `AnimatableDouble` from `_state.PlayerOrganization.Resources`).

### 3. Regression check on country info panel

Select a country with characters, influence, and country actions. Open the characters and actions slides. Confirm counts and values match what they showed before the refactor.

## Tests

The following changes to `src/` tests are required:

- **`Game.Tests/ActionSystemTests.cs` (new)** — unit tests for the merged `ActionSystem`:
  - `ProcessPlayAction` returns `Executed=true, Success=true/false` with correct `ResourceChange` effect.
  - `ProcessPlayCountryAction` returns correct `InfluenceAdded` and `CharacterOpinionChange` effects when successful.
  - Failed actions return empty `Effects` list.
- **`Game.Tests/EffectsTests.cs` (new)** — trivial construction/type-check tests for the three `IEffect` implementations.
- **`Game.Tests/ResourceSystemTests.cs`** — if any assertions reference `ResourceStateEntry.Value` as `double`, update to call `.Actual` or `.Display`.
- **Existing `ActionSystem` tests** — update any test that checks `result.GoldSpent` or `result.InfluenceAdded` to instead assert the corresponding entry in `result.Effects`.

## Constitution Check

No conflicts found. All game logic changes remain in `src/` (ECS, pure C#); Unity-side changes are limited to MonoBehaviour/view bindings; no Canvas or uGUI is introduced; VContainer remains the sole DI mechanism; one `.asmdef` per feature folder is unchanged; tabs and `_`-prefix conventions are maintained throughout.

---

Use /implement to start working on the plan or request changes.
