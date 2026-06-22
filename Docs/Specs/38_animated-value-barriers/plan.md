# Plan: Animated Value Barriers

## Spec

When a player plays a card, gold, influence, and character opinion values currently snap immediately to their new values. This feature introduces animation barriers that hold a displayed value at its pre-action level and then animate it smoothly to the actual game value over a configurable duration, making changes legible and responsive. Gold uses a smooth lerp (AnimatableDouble) and animates in parallel with the card fly over 0.5 s. Influence and opinion use integer step-counting (AnimatableInt) and animate over 1 s after the card fly completes. On a failed roll, held barriers are cancelled immediately. Barriers survive SetActual calls from VisualStateConverter, and AnimationBarrierDriver ticks every Unity frame including while ECS is paused. Multiple concurrent barriers on the same value are summed: Display = Actual + sum(barrier.Offset).

## Goal

Add smooth animated value transitions for gold, influence, and character opinion when a card is played.

## Approach

Two typed barrier classes (AnimationBarrierDouble, AnimationBarrierInt) live in `src/Game.Main/` alongside matching animatable value wrappers (AnimatableDouble, AnimatableInt). A pure-C# VContainer ITickable (AnimationBarrierDriver, in the Unity layer) drives all active barriers each frame by calling their Tick(deltaTime) method. CardPlayAnimator calls Hold() on each relevant animatable before pushing commands and calls Release() — which returns a UniTask — at the correct timing; the sequence awaits UniTask.WhenAll on all release tasks before clearing _isPlaying.

## Agent Steps

- [ ] **Create AnimatableDouble** — Add `src/Game.Main/AnimatableDouble.cs`. Pure C# class with `double Actual`, `IReadOnlyList<AnimationBarrierDouble> ActiveBarriers`, `double Display` (= Actual + sum of barrier offsets), and `void SetActual(double value)` that updates Actual without cancelling barriers. Implements INotifyPropertyChanged; fires PropertyChanged whenever Display changes (on SetActual and on barrier tick).

- [ ] **Create AnimatableInt** — Add `src/Game.Main/AnimatableInt.cs`. Same shape as AnimatableDouble but typed to int. `int Display` rounds the summed offset to the nearest integer. Fires PropertyChanged on SetActual and on barrier tick.

- [ ] **Create AnimationBarrierDouble** — Add `src/Game.Main/AnimationBarrierDouble.cs`. Constructor takes `double offset` (the held delta, negative for a spend) and `float duration`. Exposes `double Offset` (current held offset), `bool IsComplete`, and `void Tick(float deltaTime)` that lerps Offset toward 0 over the duration. Exposes `UniTask Release()` that returns a task completing when IsComplete is true (polls each frame via UniTask.NextFrame loop). AnimatableDouble's Hold() method instantiates one of these with the given offset, adds it to the active list, and returns it.

- [ ] **Create AnimationBarrierInt** — Add `src/Game.Main/AnimationBarrierInt.cs`. Same structure as AnimationBarrierDouble but Offset is int and Tick() decrements by integer steps proportional to deltaTime/duration (no partial steps — accumulate fractional steps the same way GameTime accumulates hours). Release() completes when Offset reaches 0.

- [ ] **Add animatable fields to VisualState** — In `src/Game.Main/VisualState.cs`, add three public properties to the `VisualState` class:
  - `AnimatableDouble PlayerGold { get; } = new AnimatableDouble();`
  - `AnimatableInt PlayerInfluenceInSelectedCountry { get; } = new AnimatableInt();`
  - `Dictionary<string, AnimatableInt> CharacterOpinions { get; } = new Dictionary<string, AnimatableInt>();`
  These are not sub-state classes; they are the animated value wrappers the driver and views read directly.

- [ ] **Wire SetActual in VisualStateConverter** — In `src/Game.Main/VisualStateConverter.cs`:
  - In `UpdateResources()`: after building the player resource list, find the entry with `ResourceId == "gold"` and call `_state.PlayerGold.SetActual(goldEntry.Value)`.
  - In `UpdateSelectedInfluence()`: after building entries, find the entry matching `_state.PlayerOrganization.OrgId` and call `_state.PlayerInfluenceInSelectedCountry.SetActual(entry?.Influence ?? 0)`.
  - In `UpdateCharacters()`: after building `entries`, for each `CharacterStateEntry` call `_state.CharacterOpinions.GetOrCreate(charId).SetActual(entry.Opinion)` (use a helper that does `TryGetValue` then `new AnimatableInt()` on miss). Remove keys no longer in the character list to avoid stale entries.

- [ ] **Create AnimationBarrierDriver** — Add `Assets/Scripts/Unity/UI/AnimationBarrierDriver.cs`. Implements `ITickable` (VContainer.Unity). Injected with `VisualState _state`. `Tick()` calls `deltaTime = Time.deltaTime` and iterates all active barriers on `_state.PlayerGold`, `_state.PlayerInfluenceInSelectedCountry`, and each value in `_state.CharacterOpinions`, calling `barrier.Tick(deltaTime)` and removing completed barriers from the active list. After ticking, fires the animatable's PropertyChanged so listeners re-read Display. Driver must be registered in GameLifetimeScope.

- [ ] **Register AnimationBarrierDriver in GameLifetimeScope** — In `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`, add `builder.RegisterEntryPoint<AnimationBarrierDriver>();` alongside the other entry points.

- [ ] **Update ResourcesView to use AnimatableDouble for gold display** — In `Assets/Scripts/Unity/UI/ResourcesView.cs`, change `Refresh(CountryResourcesState state)` so that the gold label uses `Display` from the animatable value instead of `resource.Value`. The Refresh signature gains an optional `AnimatableDouble playerGold = null` parameter (or the caller always passes it). When building the gold row: `label.text = playerGold != null ? $"{playerGold.Display:F0}" : $"{resource.Value:F0}"`. ResourcesView is called from PlayerOrgView.Refresh — update PlayerOrgView.Refresh to receive and forward the AnimatableDouble from VisualState.

- [ ] **Update CharactersView to use AnimatableInt for opinion display** — In `Assets/Scripts/Unity/UI/CharactersView.cs`, change `Refresh(CountryCharactersState state)` to accept `Dictionary<string, AnimatableInt> opinions = null`. When building `opinionText`, look up the AnimatableInt by `entry.CharacterId` and use `.Display` if present, otherwise fall back to `entry.Opinion`.

- [ ] **Update CountryInfoView and HUDDocument call sites** — `CountryInfoView.Refresh()` already receives `CountryCharactersState characters`. Update its signature to also accept `Dictionary<string, AnimatableInt> opinions` and forward it to `_charactersView.Refresh(characters, opinions)`. In `HUDDocument`, pass `_state.CharacterOpinions` in all `_countryInfo.Refresh(...)` calls.

- [ ] **Wire barriers in CardPlayAnimator — gold** — In `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`:
  - Inject `AnimationBarrierDriver _barrierDriver` (add to `[Inject] void Construct(...)`).
  - In `PlaySequence` (org action): before pushing commands, read the gold cost from `_actionConfig.Find(actionId)` cost entries (same as `GetGoldCostText` does). Call `var goldBarrier = _state.PlayerGold.Hold(-goldCost, 0.5f)` immediately before `_commands.Push(new PlayActionCommand {...})`. Store the UniTask from `goldBarrier.Release()` but do not await it yet.
  - After the card fly (`await _transitionView.Show(...)` returns), await the gold release task if it is not yet complete.
  - On failed roll: call `_state.PlayerGold.CancelAll()` (add a CancelAll() method to AnimatableDouble that zeroes all barrier offsets and marks them complete).

- [ ] **Wire barriers in CardPlayAnimator — influence and opinion (country action)** — In `PlayCountrySequence`:
  - Before pushing `PlayCountryActionCommand`, capture the influence cost from the matching `CountryActionCardEntry` (`InfluenceBase + InfluenceBonus`). Call `var infBarrier = _state.PlayerInfluenceInSelectedCountry.Hold(influenceCost, 1.0f)` (positive offset since spending adds to used-influence display) and, if the action has an opinion effect and `targetCharId` is known, `var opinBarrier = _state.CharacterOpinions.GetOrCreate(targetCharId).Hold(-expectedOpinionChange, 1.0f)`.
  - After the card-to-deck fly, await `UniTask.WhenAll(infBarrier.Release(), opinBarrier?.Release() ?? UniTask.CompletedTask)`.
  - On failed roll (success == false): call CancelAll on both barriers immediately.
  - Keep `_isPlaying = false` after `UniTask.WhenAll` on all barrier release tasks is complete.

- [ ] **Update GS.Unity.UI.asmdef if needed** — Check `Assets/Scripts/Unity/UI/GS.Unity.UI.asmdef` references. AnimationBarrierDriver uses `Time.deltaTime` (UnityEngine) and ITickable (VContainer). VContainer GUID `b0214a6008ed146ff8f122a6a9c2f6cc` is already referenced per project rules; confirm and add if absent. No new asmdef needed.

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
