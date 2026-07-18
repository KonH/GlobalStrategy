# Plan: Recruits Resource Per Country

## Spec

Source: `Docs/Specs/26_07_18_15_recruits-resource/spec.md`.

**Intent.** Give each country a `recruits` resource that seeds at `recruitsInitialPercent%` of its `country_population`, grows monthly by `recruitsMonthlyIncreasePercent%` of the *current* `country_population`, and is clamped to `recruitsCapPercent%` of it — implemented entirely as two `IResourceCollector` implementations plugged into the pipeline from `Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md`. No new mechanism, no new `VisualState`/`VisualStateConverter` code: total value and last-applied-delta both surface through the existing generic `Resource`/`ResourceEffect` display path (`BuildResources`/`BuildEffects`) the same way gold already does.

**Dependency.** `Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md` (not yet implemented) — provides `IResourceCollector`, `ResourceCollectorRegistry`, the `resourceIdUpdateOrder`-driven resolve-then-apply sequencing in `ResourceSystem`, and the `country_population` resource this feature reads. **Implementation of this plan cannot start before that one lands** — every type/method referenced below (`ResourceCollectorRegistry.CreateDefault`, `ResourceCollector` component, the ordered `ResourceSystem.Update` overload, `country_population`) is defined there, not here.

**Key acceptance criteria (design targets):**
- `recruits_seed` (`Instant`, collector-driven): fires once at init, sets `recruits = recruitsInitialPercent% × country_population`, self-destructs (existing `Instant`-effect behavior).
- `recruits_growth` (`Monthly`, collector-driven): each month, `cap = recruitsCapPercent% × country_population`, `rawDelta = recruitsMonthlyIncreasePercent% × country_population`, applied delta `= max(0, min(rawDelta, cap - currentValue))` — never negative, so `recruits` never shrinks even if `country_population` (and its cap) drops.
- `recruits` appended to `resourceIdUpdateOrder` after `country_population`, so both collectors always read this pass's already-aggregated total.
- Total value (`recruits.Value`) and last applied delta (`recruits_growth` effect's `Value`, post-resolve) both already readable via the existing generic `VisualState.SelectedCountry.Resources` path — zero new state.
- Three new `GameSettings`/`game_settings.json` tunables: `recruitsInitialPercent` (default `5`), `recruitsCapPercent` (default `15`), `recruitsMonthlyIncreasePercent` (default `1`).
- Persists via the ordinary `[Savable]` `Resource`/`ResourceEffect` mechanism, correct immediately on load, no forced recompute.

**Out of scope:** any UI/HUD/tooltip; any consumer of the recruits value; any change to the pipeline, `country_population`, `country_score`, `population`, or `gold`; non-uniform/scenario-driven recruitment or a debug cheat; org/character-scoped recruits; delta history beyond the single most-recent value.

## Goal

Add `recruits` as a fourth resourceId in the collector pipeline: seed it once at init from `country_population`, grow-and-cap it monthly from the same source, and confirm it surfaces through the existing generic `VisualState` resource/effect display path with no new converter code.

## Approach

### 1. Shared lookup helper (small extraction, not new scope)

Both new collectors need to read another country-owned resource's current value by `(ownerId, resourceId)` — the same linear-scan-by-owner-and-id query `CountryScoreCollector` (from the pipeline plan) and `CountryScoreSystem.GetScore` already perform inline. Once a third collector needs the identical ~10-line block, extract it once rather than tripling it:

- **`src/Game.Systems/ResourceQuery.cs`**:
  ```csharp
  public static class ResourceQuery {
      public static double GetValue(IReadOnlyWorld world, string ownerId, string resourceId) {
          int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
          foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
              ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
              Resource[] resources = arch.GetColumn<Resource>();
              int count = arch.Count;
              for (int i = 0; i < count; i++) {
                  if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
                      return resources[i].Value;
                  }
              }
          }
          return 0;
      }
  }
  ```
- If implemented after the pipeline plan has already landed `CountryScoreCollector`/`CountryScoreSystem.GetScore` with their own inline versions of this lookup, refactor both to call `ResourceQuery.GetValue` instead, so there is exactly one implementation of "look up a country-owned resource by id" project-wide. If implemented before, `CountryScoreCollector`/`GetScore` are written directly against `ResourceQuery.GetValue` from the start (no retrofit needed) — check which order implementation actually happened in before writing this file.

### 2. Two new collectors (`src/Game.Systems/`)

- **`RecruitsSeedCollector`** (`Id = "recruits_seed"`) — constructor takes `double initialPercent`; `Compute(ownerId, currentValue, world)`:
  ```csharp
  double population = ResourceQuery.GetValue(world, ownerId, "country_population");
  return (population * initialPercent / 100.0) - currentValue;
  ```
  (`currentValue` is always `0` in practice, since this is an `Instant` effect that fires once then self-destructs — written against the general delta contract for consistency with every other collector, not because it needs to handle a nonzero starting point.)
- **`RecruitsGrowthCollector`** (`Id = "recruits_growth"`) — constructor takes `double increasePercent, double capPercent`; `Compute(ownerId, currentValue, world)`:
  ```csharp
  double population = ResourceQuery.GetValue(world, ownerId, "country_population");
  double cap = population * capPercent / 100.0;
  double rawDelta = population * increasePercent / 100.0;
  return Math.Max(0.0, Math.Min(rawDelta, cap - currentValue));
  ```
  `cap - currentValue` is negative once `currentValue` already exceeds a freshly-shrunk `cap` (population loss) — the outer `Math.Max(0.0, ...)` is what guarantees recruits never shrinks, per the spec's explicit "never negative" acceptance criterion.

### 3. `ResourceCollectorRegistry.CreateDefault` extension

The pipeline plan defines `CreateDefault(double populationGrowthPercentPerMonth, double countryScoreCoefficient)`. Extend its parameter list (do not add a second `CreateDefault` overload — there is exactly one registry, one call site in `GameLogic`, and no scenario needs the recruits-less variant) to:
```csharp
public static ResourceCollectorRegistry CreateDefault(
    double populationGrowthPercentPerMonth, double countryScoreCoefficient,
    double recruitsInitialPercent, double recruitsCapPercent, double recruitsMonthlyIncreasePercent) {
    var registry = new ResourceCollectorRegistry();
    registry.Register(PopulationGrowthCollector.Id, new PopulationGrowthCollector(populationGrowthPercentPerMonth));
    registry.Register(CountryPopulationCollector.Id, new CountryPopulationCollector());
    registry.Register(CountryScoreCollector.Id, new CountryScoreCollector(countryScoreCoefficient));
    registry.Register(RecruitsSeedCollector.Id, new RecruitsSeedCollector(recruitsInitialPercent));
    registry.Register(RecruitsGrowthCollector.Id, new RecruitsGrowthCollector(recruitsMonthlyIncreasePercent, recruitsCapPercent));
    return registry;
}
```

### 4. `InitSystem` — create `recruits` entities

New `CreateRecruitsEntities(World world, CountryEntry entry)` in `src/Game.Main/InitSystem.cs`, called from the same country-seeding loop as `CreateCountryPopulationEntities` (order relative to it within the loop does not matter — both are entity *creation* only, no cross-reads happen until `ResourceSystem.Update` runs after `InitSystem.Run` completes, per the pipeline plan's ordering note): create `Resource{ResourceId="recruits", Value=0}` (`ResourceOwner(entry.CountryId, OwnerType.Country)`), one `Instant` `ResourceEffect` linked via `ResourceLink("recruits")` carrying `ResourceCollector { CollectorId = RecruitsSeedCollector.Id }`, and one `Monthly` `ResourceEffect` linked the same way carrying `ResourceCollector { CollectorId = RecruitsGrowthCollector.Id }`.

### 5. Config

- **`src/Game.Configs/GameSettings.cs`**: add `RecruitsInitialPercent` (default `5.0`), `RecruitsCapPercent` (default `15.0`), `RecruitsMonthlyIncreasePercent` (default `1.0`) as `double` properties, alongside `CountryScoreCoefficient`. Extend the default `ResourceIdUpdateOrder` array (introduced by the pipeline plan) to append `"recruits"`: `{ "population", "country_population", "country_score", "recruits" }`.
- **`Assets/Configs/game_settings.json`**: add `"recruitsInitialPercent": 5`, `"recruitsCapPercent": 15`, `"recruitsMonthlyIncreasePercent": 1`; extend the `resourceIdUpdateOrder` array (already added by the pipeline plan) to include `"recruits"` as the fourth entry.

### 6. `GameLogic` wiring

Constructor: extend the existing `ResourceCollectorRegistry.CreateDefault(...)` call (added by the pipeline plan) with the three new settings values: `ResourceCollectorRegistry.CreateDefault(settings.PopulationGrowthPercentPerMonth, settings.CountryScoreCoefficient, settings.RecruitsInitialPercent, settings.RecruitsCapPercent, settings.RecruitsMonthlyIncreasePercent);`. No other `GameLogic` change — `_resourceIdUpdateOrder` already reads the (now-extended) `settings.ResourceIdUpdateOrder` array as-is, and the existing `ResourceSystem.Update(_world, _previousTime, currentTime, _resourceCollectorRegistry, _resourceIdUpdateOrder);` call site is unchanged.

### 7. No `VisualState`/`VisualStateConverter` change

Confirmed against `src/Game.Main/VisualStateConverter.cs`: `BuildResources(world, countryId)` matches any `Resource` whose `ResourceOwner.OwnerId == countryId` regardless of resourceId, and `BuildEffects(world, countryId, resourceId)` matches any `ResourceEffect` linked to that resourceId the same way — both existing, generic, and already exercised by gold. `recruits` (owned by `countryId`) and its `recruits_growth` effect (linked via `ResourceLink("recruits")`, owned by `countryId`) satisfy both queries automatically. No code in this file changes.

## Steps

### Agent Steps

- [ ] **Confirm the pipeline plan has landed** — verify `IResourceCollector`, `ResourceCollectorRegistry`, `ResourceCollector`, `CountryPopulationCollector`/`country_population`, and the ordered `ResourceSystem.Update` overload all exist as described in `Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md`. If not, stop — this plan's collectors reference types that won't exist yet.
- [ ] **Add `ResourceQuery`** — `src/Game.Systems/ResourceQuery.cs`, per Approach §1. If `CountryScoreCollector`/`CountryScoreSystem.GetScore` already have their own inline version of this lookup, refactor them to call `ResourceQuery.GetValue` instead, so there is one implementation, not two-then-three.
- [ ] **Add the two recruits collectors** — `src/Game.Systems/RecruitsSeedCollector.cs`, `src/Game.Systems/RecruitsGrowthCollector.cs`, per Approach §2.
- [ ] **Extend `ResourceCollectorRegistry.CreateDefault`** — per Approach §3.
- [ ] **Create `recruits` entities at init** — `CreateRecruitsEntities` in `src/Game.Main/InitSystem.cs`, per Approach §4.
- [ ] **Add config** — three new `GameSettings` properties, extend `ResourceIdUpdateOrder`'s default; same in `Assets/Configs/game_settings.json`; per Approach §5.
- [ ] **Wire `GameLogic`** — extend the `CreateDefault` call per Approach §6.
- [ ] **Add/extend tests** — per the Tests section below.
- [ ] **Rebuild the Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release`.

### User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish its domain reload and check `read_console(types=["error"])` — this feature touches no Unity-side script; the only expected effect is updated `Assets/Plugins/Core/*.dll` files and the new `recruits*`/`resourceIdUpdateOrder` keys in `Assets/Configs/game_settings.json` being picked up cleanly.

### 2. Sanity-check initial seeding in Play mode

Enter Play mode, select a country, and confirm (via a temporary debug read of `VisualState.SelectedCountry.Resources` or the equivalent world query) that `recruits` is non-zero immediately at tick one, equal to `recruitsInitialPercent% × country_population` for that country — proving the `Instant`-effect seed path fired correctly.

### 3. Verify monthly growth and cap in Play mode

Advance time across a month boundary; confirm `recruits` increases by `recruitsMonthlyIncreasePercent% × country_population`. Keep advancing until the value should exceed `recruitsCapPercent% × country_population`; confirm it stops growing exactly at the cap rather than overshooting.

### 4. Verify the last-applied-delta reads correctly

After a month boundary, inspect the `recruits_growth` effect's exposed value (via `VisualState.SelectedCountry.Resources`'s effects list, the same generic path gold's `base_income` already surfaces through) and confirm it matches the delta actually applied that month, including reading `0` once the resource is at its cap.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case names, matching existing files).

- **New `src/Game.Tests/ResourceQueryTests.cs`:**
  - `get_value_returns_matching_owner_and_resource_id`
  - `get_value_returns_zero_when_not_found`

- **New `src/Game.Tests/RecruitsSeedCollectorTests.cs`:**
  - `compute_returns_initial_percent_of_country_population` — `country_population` seeded to a known value, `new RecruitsSeedCollector(5.0).Compute(countryId, 0.0, world)` equals `5% × population`.

- **New `src/Game.Tests/RecruitsGrowthCollectorTests.cs`:**
  - `compute_returns_raw_delta_when_under_cap` — `currentValue` well below `cap` → delta equals `increasePercent% × population` exactly.
  - `compute_clamps_to_remaining_cap_room` — `currentValue` within one raw-delta's distance of `cap` → delta equals `cap - currentValue`, not the full raw delta.
  - `compute_returns_zero_when_already_at_cap` — `currentValue == cap` → delta is `0`.
  - `compute_never_returns_negative_delta_when_population_shrinks` — `currentValue` already above the freshly recomputed (lower) `cap` (simulating a population drop since last month) → delta is `0`, not negative; confirms recruits never shrinks.

- **Extend `src/Game.Tests/InitSystemTests.cs`:**
  - `recruits_seeded_at_init_from_initial_percent_of_country_population` — after the first `GameLogic.Update`, every available country's `recruits` (via `ResourceQuery.GetValue` or a direct world scan) equals `recruitsInitialPercent% × country_population`, non-zero from tick one.
  - `recruits_grows_and_caps_across_multiple_months` — advance `GameLogic.Update` across several month boundaries with a fixed `country_population` (no ownership changes) and assert: (a) `recruits` increases by the expected raw delta each month while under cap, (b) growth stops exactly at `recruitsCapPercent% × country_population` once reached, (c) it never exceeds the cap on a subsequent month.
  - `recruits_and_last_applied_delta_correct_immediately_after_load` — save mid-growth (before reaching cap), reload via `LoadState`, assert both `recruits.Value` and the `recruits_growth` effect's `Value` (last applied delta) match the pre-save values exactly, with no forced recompute involved (mirrors the pipeline plan's equivalent `country_score` load test).

- **`src/Game.Tests/VisualStateConverterTests.cs`** (or wherever `BuildResources`/`BuildEffects` coverage would naturally live — none currently exists for any resource, gold included, confirmed via search): not added as part of this plan. `BuildResources`/`BuildEffects` are unmodified generic code already exercising the exact `ResourceOwner`/`ResourceLink`/`ResourceEffect` shapes `recruits` uses; the Play-mode User Steps above are this plan's verification of that surface, consistent with there being no existing precedent of testing it at the `VisualStateConverter` level for any other resource.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* Every new/changed type (`ResourceQuery`, `RecruitsSeedCollector`, `RecruitsGrowthCollector`, `GameSettings`, `InitSystem`, `GameLogic`) lives in `src/Game.Systems`, `src/Game.Configs`, `src/Game.Main` — no MonoBehaviour, no `Assets/Scripts/Unity/*` change.
- *VContainer sole DI.* No new registrations — `RecruitsSeedCollector`/`RecruitsGrowthCollector` are registered into the same directly-constructed `ResourceCollectorRegistry` the pipeline plan already builds inside `GameLogic`'s constructor.
- *UI Toolkit only.* No UI surface added or modified — per the spec's explicit "UI now is out of scope."
- *URP only.* No rendering/shader/material change.
- *One `.asmdef` per feature folder.* Not applicable — this feature only touches `src/` (`.csproj`-based), no `Assets/Scripts/` change.
- *Planning/Specification discipline.* Follows an approved spec (`Docs/Specs/26_07_18_15_recruits-resource/spec.md`) via the standard `/specify` → `/plan` sequence, and explicitly gates implementation start on its stated dependency (`26_07_18_17_resource-collector-pipeline`) landing first.
- *File organisation.* Plan lives at `Docs/Specs/26_07_18_15_recruits-resource/plan.md`, matching its spec's directory.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching all surrounding files referenced in this plan and in the pipeline plan.

Use /implement to start working on the plan or request changes.
