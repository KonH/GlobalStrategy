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
string Name          — debug label only
int    Offset        — current animated offset in whole units; ticks toward 0
int    InitialOffset — snapshot at creation; total steps to animate
```

### AnimatableDouble / AnimatableInt (pure C#, persistent on VisualState)

Two typed classes with the same shape:

```
T      Actual     — set by VisualStateConverter each tick
T      Display    — computed: Actual + sum(barrier.Offset)
List<AnimationBarrier>  — attached barriers; survive SetActual
INotifyPropertyChanged  — fires on SetActual and on Detach
Attach(name, offset) → AnimationBarrier
Detach(barrier)      → removes, fires PropertyChanged
NotifyDisplayChanged()  — called by driver each frame while barriers tick
```

Instantiated once; live for the lifetime of `VisualState`. `VisualStateConverter` never recreates them.

**Step animation:** the driver advances `barrier.Offset` by whole integer steps, not a smooth lerp. Each frame it calculates `stepsThisFrame = floor(elapsed / duration * abs(InitialOffset)) - stepsAlreadyApplied` and decrements offset by that many units. This produces a counting effect (e.g. influence ticking 10 → 9 → 8 … → 0).

### AnimationBarrierDriver (pure C#, ITickable)

Registered with VContainer as an entry point. `Tick()` is called every Unity frame by VContainer independently of ECS pause state. Reads `Time.deltaTime` directly.

```
Hold(animatable, int offset, string name) → AnimationBarrier
Release(AnimationBarrier, float duration) → UniTask
AutoRelease(animatable, int offset, float duration, string name) → UniTask
Cancel(AnimationBarrier) — removes immediately, no animation
```

On completion per barrier: calls `animatable.Detach(barrier)` and resolves the `UniTask`.

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

## Resolved Design Decisions

- **Influence field:** The player org's used influence in the selected country is the animated value.

- **Typed classes:** Use separate `AnimatableDouble` and `AnimatableInt` classes rather than a single generic. All three animated values (gold, influence, opinion) animate in **integer steps of 1** — the offset decrements by whole units per tick, producing a counting effect rather than a smooth lerp. The driver calculates the number of steps to advance each frame from `elapsed / duration * totalSteps`.

- **Driver as pure C# `ITickable`:** `AnimationBarrierDriver` is a plain C# class registered with VContainer as an entry point (`ITickable`). No MonoBehaviour or GameObject required. VContainer calls `Tick()` every Unity frame independently of ECS pause state. `Time.deltaTime` is read inside `Tick()` (Unity layer dependency is acceptable here).
