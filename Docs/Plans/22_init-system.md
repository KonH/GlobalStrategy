# Plan 22 — Init System Refactoring

## Goal

Remove all entity creation from the `GameLogic` constructor. Instead, a single-shot `InitSystem` creates all initial entities on the first `Update()` call, guarded by an `IsInitialized` marker component. `LoadSystem.Apply` calls `world.DestroyAll()` then restores savable entities from the snapshot — including `IsInitialized` if it was saved — so `InitSystem` correctly skips re-creation after a load.

This plan must be implemented before Plan 21 (Characters), as Plan 21 adds entity creation that should go into `InitSystem` from the start.

## Approach

1. Add an `IsInitialized` marker component (`[Savable]`, no fields).
2. Create `InitSystem` in `Game.Systems` — moves all entity-creation logic out of the constructor.
3. Slim down `GameLogic` constructor to config loading and helper-object setup only.
4. Call `InitSystem.Update(...)` at the top of `GameLogic.Update()` before all other systems; refresh singleton entity IDs when init runs.

The invariant: `IsInitialized` is absent on a fresh world → `InitSystem` runs and creates everything. After a successful load, `LoadSystem.Apply` restores `IsInitialized` from the snapshot → `InitSystem` is a no-op on the next `Update()`.

---

## Steps

### Step 1 — `IsInitialized` component (`src/Game.Components/IsInitialized.cs`)

Create a new file:

```csharp
namespace GS.Game.Components {
    [Savable]
    public struct IsInitialized { }
}
```

No fields. Being `[Savable]` means it is included in the snapshot and restored by `LoadSystem.Apply`, which is what prevents `InitSystem` from re-running after a load.

### Step 2 — `InitSystem` (`src/Game.Systems/InitSystem.cs`)

Create a static system class. It receives `World`, `GameLogicContext`, and a `Random` instance. Returns `true` if initialization was performed (so the caller can refresh singleton entity IDs), `false` if the world was already initialized.

```csharp
public static class InitSystem {
    public static bool Update(World world, GameLogicContext context, Random rng) {
        int[] required = { TypeId<IsInitialized>.Value };
        foreach (var arch in world.GetMatchingArchetypes(required, null)) {
            if (arch.Count > 0) return false;
        }
        Run(world, context, rng);
        return true;
    }

    static void Run(World world, GameLogicContext context, Random rng) {
        // ... all entity creation from GameLogic constructor (see Step 3) ...

        int initEntity = world.Create();
        world.Add(initEntity, new IsInitialized());
    }
}
```

Move the following entity-creation logic from `GameLogic` constructor into `Run`:

- **Country + resource entities**: the `foreach (var entry in countryConfig.Countries)` loop including `CreateResourceEntities` calls
- **GameTime entity**: `_gameTimeEntity = world.Create(); world.Add(...)` 
- **Locale entity**: `_localeEntity = world.Create(); world.Add(...)`
- **AppSettings entity**: `_settingsEntity = world.Create(); world.Add(...)`
- **Organization entity + gold entity + influence entity**: the `if (orgEntry != null)` block

`InitSystem` does not return entity IDs — the caller refreshes them via `RefreshSingletonEntities()` after the system runs (see Step 4).

The `CreateResourceEntities` helper can be moved as a private static method into `InitSystem`, or kept as a private static helper passed to it. Prefer moving it entirely — it has no dependency on `GameLogic` instance state.

### Step 3 — Slim down `GameLogic` constructor (`src/Game.Main/GameLogic.cs`)

The constructor retains only setup that does **not** touch the ECS world:

```csharp
public GameLogic(GameLogicContext context) {
    _context = context;
    _visualStateConverter = new VisualStateConverter(VisualState);
    Commands = (IWriteOnlyCommandAccessor)_commandAccessor;
    _rng = new Random();

    // Config loading only — no world.Create() calls
    ResourceConfig = context.Resource.Load();
    _countryConfig  = context.Country.Load();
    _orgConfig      = context.Organization.Load();
    var settings    = context.GameSettings.Load();
    _speedMultipliers = settings.SpeedMultipliers;
}
```

Add `_rng` as a field (`readonly Random _rng`). Cache the loaded configs that `InitSystem` needs (`_countryConfig`, `_orgConfig`, `_speedMultipliers`, etc.) as fields so they can be passed to `InitSystem.Update(...)` in `Update()`.

Remove the `_gameTimeEntity`, `_localeEntity`, `_settingsEntity`, `_orgEntity` field initializations from the constructor. Initialize them to `-1`.

### Step 4 — Update `GameLogic.Update()` (`src/Game.Main/GameLogic.cs`)

At the top of `Update()`, call `InitSystem` and refresh singleton IDs if it ran:

```csharp
public void Update(float deltaTime) {
    if (InitSystem.Update(_world, _context, _rng)) {
        RefreshSingletonEntities();
    }
    // ... rest of Update unchanged ...
}
```

`RefreshSingletonEntities()` is already implemented and correct — it scans for each singleton component type and assigns the entity ID. It is now called in two places: here (after first-time init) and in `LoadState` (after a load). Both are correct.

Guard any systems that use `_gameTimeEntity` / `_localeEntity` / `_orgEntity` against `-1` — they are only `-1` before the first `Update()` call, which should not happen in practice. Add a check or rely on `InitSystem` always running first in `Update()`.

### Step 5 — Tests (`src/Game.Tests/`)

Update any existing tests that rely on entity state being present immediately after `new GameLogic(context)` without calling `Update()`:

- If a test constructs `GameLogic` and reads world state, it must call `logic.Update(0f)` first to trigger `InitSystem`.
- Search existing tests for `new GameLogic(` and check each call site.

Add a new test class `InitSystemTests`:

1. `world_is_empty_before_first_update` — after `new GameLogic(context)`, world has no Country entities.
2. `world_is_populated_after_first_update` — after `logic.Update(0f)`, Country entities exist.
3. `init_does_not_run_twice` — call `logic.Update(0f)` twice; entity count for Country is the same after both calls.
4. `init_skipped_after_load` — construct logic, call `Update(0f)` (creates entities + IsInitialized), save, call `LoadState(saveName)`, call `Update(0f)` again; entity count for Country equals the loaded count, not double.

---

## Files to Create / Modify

| Action | Path |
|---|---|
| Create | `src/Game.Components/IsInitialized.cs` |
| Create | `src/Game.Systems/InitSystem.cs` |
| Modify | `src/Game.Main/GameLogic.cs` |
| Modify | `src/Game.Tests/` (existing tests + new `InitSystemTests.cs`) |

---

## Tests

All tests in `src/Game.Tests/`, run with `dotnet test src/GlobalStrategy.Core.sln`.

`InitSystemTests` covers the three key scenarios: fresh start (init runs), double update (init runs once), and post-load (init skipped). Existing tests must be updated to call `Update(0f)` before asserting on world state.

---

Use /implement to start working on the plan or request changes.
