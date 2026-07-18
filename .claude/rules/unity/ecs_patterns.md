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

## Composition over parallel lookup entities for derived per-entity state

When a derived value belongs conceptually to an entity that already exists (e.g. a score belonging to a `Country` or an `Organization`), attach a shared, generically-named component directly onto that entity rather than creating a second entity that merely references the first by id.

```csharp
// Not [Savable] — see the [Savable] omission pattern above.
public struct Score {
    public double Value;
}
```

```csharp
// Wrong — a parallel entity keyed by a duplicated id, requiring a second lookup:
public struct CountryScore {
    public string CountryId; // duplicates Country.CountryId
    public double Value;
}

// Right — composed directly onto the existing Country entity:
int[] required = { TypeId<Country>.Value };
foreach (var arch in world.GetMatchingArchetypes(required, null)) {
    Country[] countries = arch.GetColumn<Country>();
    for (int i = 0; i < arch.Count; i++) {
        int entity = arch.Entities[i];
        double value = ComputeScore(countries[i].CountryId);
        if (world.Has<Score>(entity)) {
            world.Get<Score>(entity).Value = value;
        } else {
            world.Add(entity, new Score { Value = value });
        }
    }
}
```

This removes an entire id-keyed lookup dictionary that a parallel-entity design otherwise needs (build a `CountryId -> scoreEntity` map, then look it up per recompute) — the "score entity" and the "subject entity" are the same entity, so there is nothing to keep in sync.

**Update (`26_07_18_17_resource-collector-pipeline`): `country_score` no longer uses this `Country + Score` shape.** It moved to the generic `Resource`/`ResourceOwner` shape (`Resource{ResourceId="country_score"}` owned by the country), fed by a collector-driven `ResourceEffect` through the `ResourceSystem` pipeline, for uniformity with `population`/`gold`/`recruits` — see `CountryScoreCollector` and `CountryScoreSystem.GetScore`. `Organization + Score` above is still current and unchanged: it remains a valid example of this composition pattern, just no longer paired with `Country + Score` as a second instance of it.

## `ref` locals and lambdas

C# does not allow `ref` locals inside anonymous methods or lambdas. When iterating over commands that need to mutate a `ref` component, use `AsSpan()` and index directly:

```csharp
var span = changeSpeed.AsSpan();
if (span.Length > 0) time.MultiplierIndex = span[span.Length - 1].Index;
```
