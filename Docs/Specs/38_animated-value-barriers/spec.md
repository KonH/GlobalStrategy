# Spec: Animated Value Barriers

## Feature Intent

As a player, I want UI values (gold, influence, opinion) to animate smoothly from their old value to their new value when a card is played, so that changes feel responsive and legible rather than snapping instantly.

## Acceptance Criteria

- **Given** the player plays a card that costs gold **When** the `PlayActionCommand` is pushed **Then** the gold display value begins at `Actual + barrier.Offset` (showing the pre-spend value) and animates to `Actual` over the configured duration, never jumping.

- **Given** the player plays a card that grants influence **When** the result arrives **Then** the influence display value animates from its pre-action value to the new actual value over the configured duration.

- **Given** the player plays a card that changes a character's opinion **When** switching to the characters view **Then** the affected character's opinion display value animates from old to new over the configured duration.

- **Given** a card play fails (effect did not apply) **When** the result is checked **Then** any held influence barrier is cancelled immediately and the influence display snaps to its actual value with no animation.

- **Given** the ECS is paused (during card play) **When** barriers are ticking **Then** `AnimatableValue.Display` still updates every Unity frame because `AnimationBarrierDriver` is a MonoBehaviour, not an ECS system.

- **Given** a barrier is animating **When** `VisualStateConverter` calls `SetActual` on the same `AnimatableValue` **Then** the existing barriers survive the call and continue animating; only `Actual` is updated and `Display` is recalculated as `Actual + sum(barrier.Offset)`.

- **Given** a barrier reaches zero **When** its duration elapses **Then** it is detached from the `AnimatableValue`, `PropertyChanged` fires, and the `UniTask` returned from `Release` completes.

- **Given** `AutoRelease` is called **When** the caller uses `.Forget()` **Then** the animation runs to completion without the caller awaiting it, and the barrier is cleaned up correctly.

- **Given** multiple barriers are attached to the same `AnimatableValue` **When** each ticks independently **Then** `Display` equals `Actual + sum(all active barrier Offsets)` at every frame.

- **Given** any view that previously read a raw numeric property **When** an `AnimatableValue` replaces that property **Then** the view reads `value.Display` instead, and subscribes to `INotifyPropertyChanged` on the `AnimatableValue` as before — no other view changes are required.

## Design Notes

### AnimationBarrier (pure C#)

```
string Name       — debug label only
double Offset     — current animated offset; ticks toward 0
double InitialOffset — snapshot at creation; used as lerp start
```

### AnimatableValue (pure C#, persistent on VisualState)

```
double Actual     — set by VisualStateConverter each tick
double Display    — computed: Actual + sum(barrier.Offset)
List<AnimationBarrier>  — attached barriers; survive SetActual
INotifyPropertyChanged  — fires on SetActual and on Detach
Attach(name, offset) → AnimationBarrier
Detach(barrier)      → removes, fires PropertyChanged
NotifyDisplayChanged()  — called by driver each frame while barriers tick
```

`AnimatableValue` is instantiated once and lives for the lifetime of `VisualState`. `VisualStateConverter` never recreates it.

### AnimationBarrierDriver (MonoBehaviour, Unity layer)

Registered with VContainer. Runs every Unity frame via `Update()`.

```
Hold(AnimatableValue, double offset, string name) → AnimationBarrier
Release(AnimationBarrier, float duration) → UniTask
AutoRelease(AnimatableValue, double offset, float duration, string name) → UniTask
Cancel(AnimationBarrier) — removes immediately, no animation
```

Lerp: each frame, `barrier.Offset = Lerp(barrier.InitialOffset, 0, elapsed / duration)`. Calls `value.NotifyDisplayChanged()` each frame. On completion, calls `value.Detach(barrier)` and resolves the `UniTask`.

### VisualState additions

- `AnimatableValue Gold` — replaces or wraps current gold exposure
- `AnimatableValue Influence` — see ambiguity below
- `Dictionary<string, AnimatableValue> OpinionByCharacterId` — keyed by `characterId`

### VisualStateConverter changes

- `_state.Gold.SetActual(ecsGold)` replaces any direct assignment
- `_state.Influence.SetActual(ecsInfluence)` similarly

### CardPlayAnimator integration (primary consumer)

Sequence:
1. `Hold` gold barrier (`+goldCost`) and influence barrier (`-influenceDelta`) before pushing commands.
2. Push `PlayActionCommand` then `PauseCommand` in the same frame.
3. Await `_resultReady`.
4. On failure: `Cancel(influenceBarrier)`.
5. Gold: `var goldDone = Release(goldBarrier, 0.5f)` — runs in parallel with card fly animation.
6. After card fly: `await Release(influenceBarrier, 1.0f)`.
7. Opinion: `await Release(opinionBarrier, 1.0f)` after switching to characters view.
8. `await UniTask.WhenAll(goldDone, ...)` before clearing `_isPlaying`.

## Out of Scope

- Animations for values not touched during card play (e.g. date display, country population).
- Cancelling or interrupting an already-releasing barrier mid-animation (only `Cancel` on held barriers is required).
- Barrier persistence across scene loads.
- Easing curves — linear lerp is sufficient for this iteration.
- Any visual polish beyond reading `Display` instead of `Actual` (no colour flash, no bounce).

## Ambiguities

- [NEEDS CLARIFICATION: Which specific numeric value under `CountryInfluenceState.OrgEntries` gets an `AnimatableValue`? Candidate: the player org's used influence in the selected country. Confirm the exact field path before implementation.]

- [NEEDS CLARIFICATION: Opinion is an integer value. Should there be a separate `AnimatableInt` typed class (with `int Display`), or is it acceptable to cast `double Display` to `int` in the view? Using `AnimatableValue` (double) with a cast keeps the implementation to one class but loses type safety at the view layer.]

- [NEEDS CLARIFICATION: Which GameObject in the scene should host `AnimationBarrierDriver`? Candidate: the existing HUD GameObject, or a dedicated `AnimationBarrierDriver` GameObject under the scene root. Confirm placement to avoid ambiguity in VContainer registration.]
