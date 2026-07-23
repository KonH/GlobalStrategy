---
paths:
  - "src/**"
---

# ECS Patterns

## No system-to-system calls

A system (a static class with an `Update`/`Seed`/etc. entry point invoked from the top-level
game loop) must never call another system's entry point from inside its own logic. Only the
top-level orchestrator (`GameLogic.Update`, or the one-time bootstrap path it drives via
`InitSystem.Update`) is allowed to call systems — that is its entire job. A system may call a
plain, non-system helper method (private, or a pure query like `ResourceQuery.GetValue`), but
not another system's public entry point.

This keeps every system callable in exactly one place with exactly one contract, instead of
letting call graphs form between systems where the caller has to know the callee's internal
timing/ordering assumptions (e.g. "this only works if called with `previousTime == currentTime`").

**Self-gate one-time initialization instead of a special bootstrap call.** Rather than having
`InitSystem` reach into another system to force a first-time seed, prefer a shape where the
system's own regular per-tick invocation is naturally idempotent:

- If the system is driven by data whose existence is itself the "already initialized" signal
  (e.g. a `PayType.Instant` `ResourceEffect`, which applies unconditionally the first time it's
  processed and then self-destroys), no special-case call is needed at all — the system's normal
  per-tick call, which the top-level loop already makes right after `InitSystem.Update` in the
  same tick, does the seeding on its own.
- Otherwise, gate the one-time work behind the same singleton marker component `InitSystem`
  already uses (`IsInitialized`) — call the other system from the top-level orchestrator inside
  the `if (InitSystem.Update(...))` branch, not from within `InitSystem.Run` itself. `InitSystem`
  itself should only ever create raw entity/component data, never call into another system.

```csharp
// Wrong — InitSystem (a system) calling another system's entry point from inside its own logic:
static void Run(World world, GameLogicContext context, Random rng) {
    ...
    ProvinceOwnershipSystem.Seed(world, provinceConfig);
    ...
    ResourceSystem.Update(world, startTime, startTime, registry, order); // forces a bootstrap pass
}

// Right — InitSystem only creates data; the top-level loop calls other systems, gated by the
// same IsInitialized marker InitSystem already exposes via its return value:
public void Update(float deltaTime) {
    if (InitSystem.Update(_world, _context, _rng)) {
        RefreshSingletonEntities();
        ProvinceOwnershipSystem.Seed(_world, ProvinceConfig); // one-time, gated by IsInitialized
    }
    ...
    ResourceSystem.Update(_world, _previousTime, currentTime, _resourceCollectorRegistry, _resourceIdUpdateOrder);
    // ^ runs unconditionally every tick, including this same tick — Instant effects created
    // during InitSystem.Run apply themselves the first time this runs, then self-destroy.
}
```

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

**Update (`26_07_18_17_resource-collector-pipeline`): the standalone `Score` component shown above no longer exists.** Both `country_score` and `org_score` moved to the generic `Resource`/`ResourceOwner` shape (`Resource{ResourceId="country_score"|"org_score"}` owned by the country/org), each fed by a collector-driven `ResourceEffect` through the `ResourceSystem` pipeline, for uniformity with `population`/`gold`/`recruits` — see `CountryScoreCollector`/`OrgScoreCollector` and `ResourceQuery.GetValue`. There is no `CountryScoreSystem`/`OrgScoreSystem` wrapper either; callers query `ResourceQuery.GetValue` directly, same as any other resource. The `Score`/`world.Has<Score>`/`world.Add(entity, new Score {...})` snippet above is retained purely as an illustration of the composition-over-parallel-entity principle for a *future* derived value that doesn't fit the `Resource` shape — do not reintroduce it for scores.

## `ref` locals and lambdas

C# does not allow `ref` locals inside anonymous methods or lambdas. When iterating over commands that need to mutate a `ref` component, use `AsSpan()` and index directly:

```csharp
var span = changeSpeed.AsSpan();
if (span.Length > 0) time.MultiplierIndex = span[span.Length - 1].Index;
```
