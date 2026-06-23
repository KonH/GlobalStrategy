# Animation Barrier Conventions

`AnimatableInt` and `AnimatableDouble` support barriers via `Hold(offset)`. The barrier adds `offset` to `Display` until released via `barrier.Release(duration)`, which animates the offset back to zero.

## Offset sign

Always use a **negative** offset equal to the delta that was just applied:

```csharp
animatable.Hold(-delta);
```

`Display = Actual + (-delta) = oldValue` — the value appears unchanged, then animates forward to `Actual` as the barrier decays.

Using `+delta` overshoots: `Display = newActual + delta`, then animates back down. This is the wrong direction for resource gains.

## Releasing multiple barriers in parallel

When a single action creates barriers on more than one animatable (e.g. influence **and** opinion from the same card), release them with `UniTask.WhenAll`:

```csharp
if (hasInfluence && hasOpinion) {
    barrierTask = UniTask.WhenAll(
        _barrierHolder.Animate("influence", 1.0f),
        _barrierHolder.Animate("opinion", 1.0f));
} else if (hasInfluence) {
    barrierTask = _barrierHolder.Animate("influence", 1.0f);
} else if (hasOpinion) {
    barrierTask = _barrierHolder.Animate("opinion", 1.0f);
}
```

Using `else if` silently leaves the second barrier held forever, freezing that value in the UI.

## `CardPlayBarriersHolder` usage

`AddDouble(key, animatable, offset)` and `AddInt(key, animatable, offset)` call `Hold(offset)` immediately. The barrier is live from that moment — call these only after `SetActual` has already fired, or accept that `Display` will briefly dip below zero until `SetActual` runs later in the same `VisualStateConverter.Update()` call (not visible to the player since rendering happens after Update completes).
