# Plan: Animated Value Barriers

## Spec

When a player plays a card, gold, influence, and character opinion values currently snap immediately to their new values. This feature introduces animation barriers that hold a displayed value at its pre-action level and then animate it smoothly to the actual game value over a configurable duration, making changes legible and responsive. Gold uses a smooth lerp (AnimatableDouble): the barrier is held for the full sequence duration (6 s), then on success released with a 0.5 s animation; on failure it is cancelled (snap to actual). Influence and opinion use integer step-counting (AnimatableInt) and animate over 1 s after the card fly completes; similarly held for 6 s and released or cancelled based on the roll result. Barriers survive SetActual calls from VisualStateConverter, and AnimationBarrierDriver (ITickable) ticks every Unity frame including while ECS is paused. PropertyChanged is fired by the animatable itself inside Tick — not by the driver. Multiple concurrent barriers on the same value are summed: Display = Actual + sum(barrier.Offset).

## Goal

Add smooth animated value transitions for gold, influence, and character opinion when a card is played.

## Approach

Two typed barrier classes (AnimationBarrierDouble, AnimationBarrierInt) live in `src/Game.Main/` alongside matching animatable value wrappers (AnimatableDouble, AnimatableInt). A pure-C# VContainer ITickable (AnimationBarrierDriver, in the Unity layer) drives all active barriers each frame by calling their Tick(deltaTime) method. CardPlayAnimator calls Hold() on each relevant animatable before pushing commands and calls Release() — which returns a UniTask — at the correct timing; the sequence awaits UniTask.WhenAll on all release tasks before clearing _isPlaying.

## Agent Steps

- [x] **Create AnimatableDouble** — Add `src/Game.Main/AnimatableDouble.cs`. Pure C# class with `double Actual`, a private `List<AnimationBarrierDouble>`, `double Display` (= Actual + sum of barrier offsets), `void SetActual(double value)` that updates Actual without cancelling barriers, and `void Tick(float deltaTime)` that calls `Tick(dt)` on each active barrier, removes completed ones, and fires `PropertyChanged` if `Display` changed. Also `Hold(double offset) → AnimationBarrierDouble`, `Cancel(AnimationBarrierDouble)`, and `CancelAll()`. Implements INotifyPropertyChanged; `PropertyChanged` is fired only inside `SetActual` and `Tick` — never externally.

- [x] **Create AnimatableInt** — Add `src/Game.Main/AnimatableInt.cs`. Same shape as AnimatableDouble but typed to int. `int Display` is `Actual + sum(barrier.Offset)`. `Tick(float deltaTime)` advances integer-step barriers and fires `PropertyChanged` if Display changed.

- [x] **Create AnimationBarrierDouble** — Add `src/Game.Main/AnimationBarrierDouble.cs`. Constructor takes `double offset` (the held delta, negative for a spend) and `float duration`. Exposes `double Offset` (current held offset), `bool IsComplete`, and `void Tick(float deltaTime)` that lerps Offset toward 0 over the duration. Exposes `UniTask Release()` that returns a task completing when IsComplete is true (polls each frame via UniTask.NextFrame loop). AnimatableDouble's Hold() method instantiates one of these with the given offset, adds it to the active list, and returns it.

- [x] **Create AnimationBarrierInt** — Add `src/Game.Main/AnimationBarrierInt.cs`. Same structure as AnimationBarrierDouble but Offset is int and Tick() decrements by integer steps proportional to deltaTime/duration (no partial steps — accumulate fractional steps the same way GameTime accumulates hours). Release() completes when Offset reaches 0.

- [x] **Add animatable fields to VisualState** — In `src/Game.Main/VisualState.cs`, add three public properties to the `VisualState` class:
  - `AnimatableDouble PlayerGold { get; } = new AnimatableDouble();`
  - `AnimatableInt SelectedCountryUsedInfluence { get; } = new AnimatableInt();` — tracks the total used influence across all orgs (`usedTotal`), matching what the `used/pool` label displays.
  - `Dictionary<string, AnimatableInt> CharacterOpinions { get; } = new Dictionary<string, AnimatableInt>();`
  These are not sub-state classes; they are the animated value wrappers the driver and views read directly.

- [x] **Add DictionaryExtensions helper** — Add `src/Game.Main/DictionaryExtensions.cs` with a single internal static extension method:
  ```csharp
  static AnimatableInt GetOrCreate(this Dictionary<string, AnimatableInt> dict, string key) {
      if (!dict.TryGetValue(key, out var v)) { v = new AnimatableInt(); dict[key] = v; }
      return v;
  }
  ```
  Used by VisualStateConverter and CardPlayAnimator to lazily populate `CharacterOpinions`.

- [x] **Wire SetActual in VisualStateConverter** — In `src/Game.Main/VisualStateConverter.cs`:
  - In `UpdateResources()`: after building the player resource list, find the entry with `ResourceId == "gold"` and call `_state.PlayerGold.SetActual(goldEntry.Value)`.
  - In `UpdateSelectedInfluence()`: after computing `usedTotal`, call `_state.SelectedCountryUsedInfluence.SetActual(usedTotal)` — before the `_state.SelectedInfluence.Set(...)` call.
  - In `UpdateCharacters()`: after building `entries`, for each `CharacterStateEntry` call `_state.CharacterOpinions.GetOrCreate(charId).SetActual(entry.Opinion)`. After the loop, remove any keys from `_state.CharacterOpinions` that are no longer in the new entries list to avoid stale animatables.

- [x] **Create AnimationBarrierDriver** — Add `Assets/Scripts/Unity/UI/AnimationBarrierDriver.cs`. Implements `ITickable` (VContainer.Unity). Injected with `VisualState _state`. `Tick()` reads `Time.deltaTime` and calls `animatable.Tick(deltaTime)` on `_state.PlayerGold`, `_state.SelectedCountryUsedInfluence`, and each value in `_state.CharacterOpinions`. Each animatable's `Tick(dt)` internally advances all active barriers and fires `PropertyChanged` itself if `Display` changed — the driver does not fire `PropertyChanged` directly. Completed barriers are removed inside the animatable's `Tick`. Driver must be registered in GameLifetimeScope.

- [x] **Register AnimationBarrierDriver in GameLifetimeScope** — In `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, add `builder.RegisterEntryPoint<AnimationBarrierDriver>();` alongside the other entry points.

- [x] **Update ResourcesView to use AnimatableDouble for gold display** — In `Assets/Scripts/Unity/UI/ResourcesView.cs`, add `AnimatableDouble? _playerGoldAnimatable` as a constructor parameter (stored as a field). `Refresh(CountryResourcesState state)` signature is unchanged; when building the gold row, use `_playerGoldAnimatable?.Display ?? resource.Value` for the label text. `PlayerOrgView` constructs `ResourcesView` — update its constructor to receive `AnimatableDouble` from the caller and forward it. `HUDDocument` passes `_state.PlayerGold` once when constructing `PlayerOrgView`. No per-`Refresh` parameter changes needed.

- [x] **Update CharactersView to use AnimatableInt for opinion display** — In `Assets/Scripts/Unity/UI/CharactersView.cs`, store `Dictionary<string, AnimatableInt>? _characterOpinions` as a constructor field (passed from the caller at construction time). `Refresh(CountryCharactersState state)` signature is unchanged; when building `opinionText`, look up by `entry.CharacterId` and use `.Display` if found, otherwise fall back to `entry.Opinion`. `CountryInfoView` constructs `CharactersView` — update to receive and forward `_state.CharacterOpinions`. `HUDDocument` passes `_state.CharacterOpinions` once at construction.

- [x] **Subscribe HUDDocument to animated influence separately** — In `HUDDocument.OnEnable`, add a subscription to `_state.SelectedCountryUsedInfluence.PropertyChanged` that calls a targeted `RefreshInfluenceDisplay()` helper — no `IsPlaying` guard on this path, since these per-frame ticks ARE the animation. `RefreshInfluenceDisplay()` updates only the influence label in `CountryInfoView` using `_state.SelectedCountryUsedInfluence.Display` for the `used` value. The existing `HandleInfluenceChanged` (subscribed to `_state.SelectedInfluence.PropertyChanged`) retains its `IsPlaying` guard for full `CountryInfoView` rebuilds.

- [x] **Wire barriers in CardPlayAnimator — gold** — In `Assets/Scripts/Unity/UI/CardPlayAnimator.cs` (no new injection needed — `AnimationBarrierDriver` is a passive `ITickable` and `CardPlayAnimator` operates directly on `_state.PlayerGold`):
  - In `PlaySequence` (org action): before pushing commands, read the gold cost from `_actionConfig.Find(actionId)`. Call `var goldBarrier = _state.PlayerGold.Hold(-goldCost, 6.0f)` — use a duration of 6 s (longer than the full sequence) so the barrier is still live when the roll result is known.
  - After the roll result is known and `success == true`: call `var goldDone = goldBarrier.Release(0.5f)` which restarts the offset animation from its current value to 0 over 0.5 s.
  - On failed roll (`success == false`): call `_state.PlayerGold.Cancel(goldBarrier)` to snap the display to the actual value immediately.
  - `await goldDone` (or confirm it is already complete) before clearing `_isPlaying`.
  - Add `Cancel(AnimationBarrierDouble barrier)` to `AnimatableDouble` and `CancelAll()` as a convenience — both zero the offset and remove the barrier.

- [x] **Wire barriers in CardPlayAnimator — influence and opinion (country action)** — In `PlayCountrySequence`:
  - Before pushing `PlayCountryActionCommand`, capture the influence cost from the matching `CountryActionCardEntry` (`InfluenceBase + InfluenceBonus`). Call `var infBarrier = _state.SelectedCountryUsedInfluence.Hold(influenceCost, 6.0f)` (positive offset — spending increases used-influence). If the action has an opinion effect and `targetCharId` is known, call `var opinBarrier = _state.CharacterOpinions.GetOrCreate(targetCharId).Hold(-expectedOpinionChange, 6.0f)`.
  - After the card-to-deck fly and on `success == true`: release both — `var infDone = infBarrier.Release(1.0f)` and `var opinDone = opinBarrier?.Release(1.0f) ?? UniTask.CompletedTask`. Await both.
  - On failed roll (`success == false`): call `_state.SelectedCountryUsedInfluence.Cancel(infBarrier)` and `_state.CharacterOpinions.GetOrCreate(targetCharId).Cancel(opinBarrier)` to snap immediately.
  - Keep `_isPlaying = false` only after `UniTask.WhenAll(goldDone, infDone, opinDone)` completes.

- [x] **Update GS.Unity.UI.asmdef if needed** — Check `Assets/Scripts/Unity/UI/GS.Unity.UI.asmdef` references. AnimationBarrierDriver uses `Time.deltaTime` (UnityEngine) and ITickable (VContainer). VContainer GUID `b0214a6008ed146ff8f122a6a9c2f6cc` is already referenced per project rules; confirm and add if absent. No new asmdef needed.

## User Steps

### 1. Build and verify Unity compilation

After the agent completes all file writes, run `dotnet build src/GlobalStrategy.Core.sln -c Release` to rebuild the DLL with the new animatable/barrier types, then open Unity and check the Console for compilation errors.

### 2. Assign AnimationBarrierDriver in the scene

VContainer registers it as an entry point automatically — no scene wiring needed. Verify in Play mode that the driver appears in the VContainer scope diagnostics.

### 3. Manual smoke test

Play a country action card while watching the gold, influence, and opinion displays. Gold should hold at its pre-spend value and slide to the new value over ~0.5 s. Influence and opinion should start animating after the card-to-deck fly. On a failed roll verify the displayed values snap back immediately.

## Tests

No existing unit tests in `src/` cover VisualState or VisualStateConverter. Add the following to the `src/` test project (or create `src/Game.Main.Tests/AnimatableValueTests.cs`):

- `AnimatableDouble_SetActual_UpdatesDisplay_WhenNoBarrier` — SetActual(10), assert Display == 10.
- `AnimatableDouble_Hold_OffsetsSummedIntoDisplay` — SetActual(100), Hold(-20, 1f), assert Display == 80.
- `AnimatableDouble_Tick_LerpsOffsetToZero` — Hold(-20, 1f), Tick(0.5f), assert Offset is between -20 and 0 and Display has moved.
- `AnimatableDouble_SetActual_DoesNotCancelBarrier` — Hold(-20, 1f), SetActual(80), assert barrier still active.
- `AnimatableDouble_CancelAll_ZeroesBarrier` — Hold(-20, 1f), CancelAll(), assert Display == Actual.
- Same set for AnimatableInt covering integer step accumulation.

## Constitution Check

No conflicts. All new pure-C# types live in `src/Game.Main/` with no UnityEngine dependency. AnimationBarrierDriver is presentation-layer glue (reads Time.deltaTime, drives Unity ITickable) — consistent with the ECS-for-logic / MonoBehaviour-for-glue rule. VContainer remains the sole DI mechanism. UI Toolkit is not changed structurally. No new asmdef is required (all new Unity-layer code goes into the existing `GS.Unity.UI` assembly). Tabs for indentation and _ prefix for private members apply throughout all new files.

Use /implement to start working on the plan or request changes.
