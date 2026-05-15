# ECS Patterns

## Singleton entities

`World` has no `GetSingleton<T>()`. For components that exist on exactly one entity (e.g. `GameTime`), store the entity ID at creation time and pass it explicitly to every system and converter that needs it.

```csharp
// In GameLogic constructor:
_gameTimeEntity = _world.Create();
_world.Add(_gameTimeEntity, new GameTime { ... });

// In Update:
TimeSystem.Update(_world, _gameTimeEntity, deltaTime, ...);
_visualStateConverter.Update(_world, _gameTimeEntity);

// In the system:
ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
```

## Fractional accumulation for slow-moving state

When advancing a value in discrete quanta (e.g. hours) driven by per-frame `deltaTime`, accumulate the fractional part in the component rather than casting each frame — otherwise small multipliers produce zero advance.

```csharp
// In the component:
public float AccumulatedHours;

// In the system:
time.AccumulatedHours += deltaTime * speedMultipliers[time.MultiplierIndex];
int hours = (int)time.AccumulatedHours;
if (hours > 0) {
    time.CurrentTime = time.CurrentTime.AddHours(hours);
    time.AccumulatedHours -= hours;
}
```

## `[Savable]` omission for derived/computed components

Components whose full state can be recomputed deterministically from config data at startup should NOT be marked `[Savable]`. Persisting them wastes save space and adds deserialization risk with no benefit. Mark the struct with a comment to make the omission intentional:

```csharp
// Not [Savable] — rebuilt at startup from config; no need to persist.
public struct ProximityMapData {
    public Dictionary<(string, string), float> Distances;
}
```

The criterion: if `InitSystem` (or an equivalent startup step) always recreates the component from scratch regardless of load state, it does not need saving.

## `ref` locals and lambdas

C# does not allow `ref` locals inside anonymous methods or lambdas. When iterating over commands that need to mutate a `ref` component, use `AsSpan()` and index directly:

```csharp
var span = changeSpeed.AsSpan();
if (span.Length > 0) time.MultiplierIndex = span[span.Length - 1].Index;
```
