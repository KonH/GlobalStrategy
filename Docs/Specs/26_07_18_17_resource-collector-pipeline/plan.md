# Plan: Resource Collector Pipeline

## Spec

Source: `Docs/Specs/26_07_18_17_resource-collector-pipeline/spec.md`.

**Intent.** Replace three independent, hand-rolled derived-value mechanisms — `ProvincePopulationGrowthSystem`'s direct compounding mutation, `CountryScoreSystem`'s bespoke aggregation-and-force-recompute, and (implicitly) any future population-driven resource — with one generic, ordered, pluggable pipeline built on the existing `ResourceEffect`/`ResourceSystem` machinery gold already uses. A new `ResourceCollector(collectorId)` component, resolved through a `ResourceCollectorRegistry` (mirroring the existing `IBotFeature`/`BotFeatureRegistry` pattern in `src/Game.Bots/`), lets an effect's `Value` be recomputed from world state every time it is processed instead of staying static. A new `resourceIdUpdateOrder` setting sequences *which* resourceId's effects resolve+apply before which other's, so a later resourceId's collector can safely read an earlier resourceId's already-updated value within the same tick. Pure `src/` ECS feature; no UI, no MonoBehaviour, no new Unity assembly.

**Dependency.** Migrates mechanics from three already-merged specs without changing their formulas: `26_07_13_17_province-population` (population growth), `26_07_14_09_country-scoring` (country score), `26_07_16_09_org-scoring` (only its *read* of country score changes).

**Key acceptance criteria (design targets):**
- `IResourceCollector.Compute(ownerId, currentValue, world)` returns a **delta**, not an absolute value — `ResourceSystem`'s `Value += delta` apply step is unchanged.
- `ResourceCollectorRegistry` mirrors `BotFeatureRegistry`'s shape (register by id, resolve by id, throw on unknown id, `CreateDefault(...)`).
- A `[Savable]` `ResourceCollector { string CollectorId }` component, attached to a `ResourceEffect` entity, causes that effect's `Value` to be recomputed by the registered collector every time it is processed; effects without it are unaffected (gold's `base_income` untouched).
- `resourceIdUpdateOrder: string[]` (`GameSettings`/`game_settings.json`) — for each listed resourceId, in order, fully resolve then fully apply its effects before moving to the next; resourceIds not listed process after, unordered, exactly as today.
- Province population growth becomes a self-referential `Monthly` collector effect on each province's own `population` resource; `ProvincePopulationGrowthSystem` is removed.
- A new resourceId `country_population`, one per country, fed by collector effects that sum the `population` of every province currently owned by that country (same live-ownership aggregation `CountryScoreSystem.Recompute` already performs).
- `country_score` (today the `Score` component composed onto `Country`) becomes `Resource{ResourceId="country_score"}`, fed by a collector reading that country's `country_population` × `countryScoreCoefficient`. `CountryScoreSystem.GetScore(world, countryId)` is preserved as a query (now reading the resource).
- `country_score` becomes `[Savable]`; the forced `CountryScoreSystem.Recompute` calls in `InitSystem`/`LoadState` are no longer needed for it and are removed.
- `OrgScoreSystem.Recompute` is updated to read country scores via `CountryScoreSystem.GetScore` instead of iterating `Country + Score` archetypes.
- `.claude/rules/unity/ecs_patterns.md`'s `Score`-on-`Country` worked example is updated to reflect the move.

**Out of scope:** migrating `org_score`/`OrgScoreSystem`'s own materialization (daily cadence, no current need); replacing `PayType` with `Instant`/`Monthly` marker components; the recruits resource itself (`26_07_18_15_recruits-resource`, a dependent plan); any change to `gold`, character skills, opinion, `ControlSystem`, or province ownership.

## Goal

Introduce `IResourceCollector`/`ResourceCollectorRegistry`/`ResourceCollector`, restructure `ResourceSystem.Update` to resolve-then-apply effects resourceId-by-resourceId in a configured order, and migrate province population growth and country score onto that pipeline — with zero behavior change to gold, character skills, opinion, or any resourceId not in the new ordered list, and full backward compatibility for every existing `ResourceSystem.Update` call site.

## Approach

### 1. New collector abstraction

- **`src/Game.Systems/IResourceCollector.cs`**:
  ```csharp
  namespace GS.Game.Systems {
      public interface IResourceCollector {
          double Compute(string ownerId, double currentValue, IReadOnlyWorld world);
      }
  }
  ```
  Stateless, pure-function contract — no per-call parameters beyond what a concrete implementation captured at construction (mirrors `IBotFeature.Tick`'s shape, simplified since collectors need no `Random`/command sink).

- **`src/Game.Systems/ResourceCollectorRegistry.cs`** — mirrors `BotFeatureRegistry` exactly, but registers ready-made singleton instances rather than factories (collectors carry no per-instantiation parameters the way `IBotFeature`s do — `populationGrowthPercentPerMonth`/`countryScoreCoefficient` are single global constants baked in once):
  ```csharp
  public sealed class ResourceCollectorRegistry {
      readonly Dictionary<string, IResourceCollector> _collectors = new();
      public void Register(string collectorId, IResourceCollector collector) => _collectors[collectorId] = collector;
      public IResourceCollector Resolve(string collectorId) {
          if (!_collectors.TryGetValue(collectorId, out var collector)) {
              throw new InvalidOperationException($"Unknown resource collector id: {collectorId}");
          }
          return collector;
      }
      public static ResourceCollectorRegistry CreateDefault(double populationGrowthPercentPerMonth, double countryScoreCoefficient) {
          var registry = new ResourceCollectorRegistry();
          registry.Register(PopulationGrowthCollector.Id, new PopulationGrowthCollector(populationGrowthPercentPerMonth));
          registry.Register(CountryPopulationCollector.Id, new CountryPopulationCollector());
          registry.Register(CountryScoreCollector.Id, new CountryScoreCollector(countryScoreCoefficient));
          return registry;
      }
  }
  ```
  `CreateDefault` takes explicit primitives (not a `GameSettings` reference) so `Game.Systems` does not need to depend on `Game.Configs` for this type — matches `BotFeatureRegistry.CreateDefault(int maxControlPool)`'s existing style of taking exactly what it needs.

- **`src/Game.Components/ResourceCollector.cs`**:
  ```csharp
  namespace GS.Game.Components {
      [Savable]
      public struct ResourceCollector {
          public string CollectorId;
      }
  }
  ```
  `[Savable]` so it round-trips automatically via `SaveSystem`'s reflection-based scan (no new save/load code needed, confirmed against `src/Game.Main/SaveSystem.cs`'s `[SavableAttribute]`-driven `_typeMap`).

### 2. Three collector implementations (`src/Game.Systems/`, one file each)

- **`PopulationGrowthCollector`** (`Id = "population_growth"`) — constructor takes `double percentPerMonth`; `Compute(ownerId, currentValue, world) => currentValue * percentPerMonth / 100.0`. Self-referential: `ownerId`/`world` unused, but present for interface uniformity. Produces the identical result as today's `resources[i].Value *= 1.0 + monthlyGrowthPercent / 100.0` (`currentValue + currentValue*pct/100 == currentValue*(1+pct/100)`).
- **`CountryPopulationCollector`** (`Id = "country_population_aggregate"`) — no constructor parameters; `Compute` calls `ProvinceOwnershipSystem.GetProvincesByOwner(world)`, builds a `provinceId -> population` lookup the same way `CountryScoreSystem.Recompute` does today (filter `OwnerType.Province` + `ResourceId == "population"`), sums the population of `ownerId`'s owned provinces, returns `freshTotal - currentValue`.
  - **Known tradeoff, deliberately accepted:** because `ResourceSystem` invokes `Compute` once per matching effect entity (once per country), this rebuilds the ownership+population lookup dictionaries from scratch per country rather than once per pass — O(countries × provinces) instead of `CountryScoreSystem.Recompute`'s current O(countries + provinces). At today's scale (163 countries, ~1200+ provinces) this is a few hundred thousand dictionary operations **once per in-game month**, not per frame — negligible in wall-clock terms, unlike the org-scoring spec's quadratic concern (which was about a query hit repeatedly per frame/interaction). Not optimized in this plan; if province/country counts grow enough to matter, `IResourceCollector` can gain an optional per-pass precompute hook later — not built speculatively here.
- **`CountryScoreCollector`** (`Id = "country_score_formula"`) — constructor takes `double coefficient`; `Compute(ownerId, currentValue, world)` looks up `Resource{ResourceId="country_population"}` for the same `ownerId` (single-entity archetype scan, cheap — no aggregation, unlike the collector above), returns `(population * coefficient) - currentValue`.

### 3. `ResourceSystem` restructuring (`src/Game.Systems/ResourceSystem.cs`)

New signature, backward-compatible via optional parameters so all 12 existing call sites (1 production in `GameLogic.cs`, 11 across `ResourceSystemTests.cs`/`ResourceEffectMaxTotalTests.cs`) compile and behave identically unchanged:

```csharp
public static void Update(
    World world, DateTime previousTime, DateTime currentTime,
    ResourceCollectorRegistry? collectorRegistry = null,
    IReadOnlyList<string>? resourceIdUpdateOrder = null) {
```

When `collectorRegistry`/`resourceIdUpdateOrder` are null/empty, behavior is byte-for-byte identical to today (no ordered pass, no collector resolution — existing tests need zero changes).

Algorithm when both are supplied:
1. For each `resourceId` in `resourceIdUpdateOrder`, in order:
   a. **Resolve** — find every `ResourceEffect` entity (via `ResourceOwner`+`ResourceLink`+`ResourceEffect`+`ResourceCollector` archetype match) whose `ResourceLink.ResourceId == resourceId` and that carries a `ResourceCollector`. For each, check today's existing `shouldApply` gate (`PayType.Instant`, or `PayType.Monthly && isMonthBoundary`) — **only resolve collectors for effects that are actually due this call**, so a `Monthly` collector effect is not recomputed (and its `Value` not overwritten) on non-boundary ticks, matching the existing `EffectStateEntry`/tooltip expectation that `Value` reflects the *last applied* amount, not a mid-month preview. Resolve by calling `collectorRegistry.Resolve(effect.CollectorId).Compute(owner.OwnerId, currentResourceValue, world)` and writing the result into `effect.Value` in place (mirrring how `MaxTotal` clamping already writes back into `effects[i]` today).
   b. **Apply** — run the existing apply step (today's `toApply`-collection-then-apply loop, `MaxTotal`/`ClampToZero` handling included, untouched) but scoped to only the effects linked to this `resourceId` (both collector-tagged, just resolved, and any plain static ones that might also target it).
2. After the ordered set, process every remaining `ResourceEffect` (any `resourceId` not in `resourceIdUpdateOrder`, or any effect with no `ResourceCollector`) with exactly today's single unordered pass — gold, character skills, opinion are entirely unaffected, both in code path and in behavior.

This means a `Monthly` collector effect's `Value`, once resolved this pass, **is** the actual delta being applied — no separate "last applied delta" bookkeeping is needed anywhere (this is what `26_07_18_15_recruits-resource`'s plan relies on for its tooltip requirement).

### 4. Province population growth migration

- **`src/Game.Main/InitSystem.cs`**, `CreateProvincePopulationEntities`: keep the existing `Resource{ResourceId="population", Value=entry.Population}` seeding line unchanged (seed value is still config data, not collector-derived), but additionally create one `Monthly` `ResourceEffect` per province, linked via `ResourceLink("population")`, carrying `ResourceCollector { CollectorId = PopulationGrowthCollector.Id }`, owned by the same `ResourceOwner(entry.ProvinceId, OwnerType.Province)`.
- **Delete** `src/Game.Systems/ProvincePopulationGrowthSystem.cs` and its test file `src/Game.Tests/ProvincePopulationGrowthSystemTests.cs` (coverage moves to `ResourceSystemTests`-style collector tests, see Tests section) — its `PopulationResourceId` constant (`"population"`) moves to a shared location (`CountryPopulationCollector`'s own file, as `public const string ResourceId = "population";`, since it is the one other place that string literal is now needed) so no other file references the deleted class.
- **`src/Game.Main/GameLogic.cs`**: remove the `ProvincePopulationGrowthSystem.Update(_world, _previousTime, currentTime, _populationGrowthPercent);` call and the `_populationGrowthPercent` field (superseded by the collector registry holding the same percentage).

### 5. `country_population` resource

- **`src/Game.Main/InitSystem.cs`**: new `CreateCountryPopulationEntities(World world, CountryConfig config)`, called from `Run` in the country-seeding loop (same place gold's resource entities are created today) — for each available country: `Resource{ResourceId="country_population", Value=0}` (`ResourceOwner(countryId, OwnerType.Country)`), one `Instant` effect and one `Monthly` effect, both linked via `ResourceLink("country_population")` and both carrying `ResourceCollector { CollectorId = CountryPopulationCollector.Id }` (the same collector serves both — its formula is identical whether it's the first-ever resolve or the fiftieth). The `Instant` effect fires on the very first `ResourceSystem.Update` call (same tick as `InitSystem.Run`, since `GameLogic.Update` calls `ResourceSystem.Update` immediately after `InitSystem.Update` returns true) and self-destructs, giving `country_population` a correct non-zero value from tick one with no forced/bypassed-gate call needed anywhere. The `Monthly` effect recurs at each subsequent month boundary.
  - **Ordering note:** entity *creation* for `country_population` has no dependency on province seeding order within `InitSystem.Run` (unlike today's `CountryScoreSystem.Recompute`, which needed province data to already exist) — the collector only reads province data when `ResourceSystem.Update` actually runs, which is always after `InitSystem.Run` completes in full. This removes a previously-real ordering constraint, not just preserves one.

### 6. `country_score` migration

- **`src/Game.Components/Score.cs`**: keep the type as-is — `Organization`'s own `Score` composition (step 7) still uses it, daily cadence, non-`[Savable]`, unchanged. Only `Country`'s use of it is removed: `CountryScoreSystem` stops composing `Score` onto `Country` entities entirely (that responsibility moves to the `Resource{ResourceId="country_score"}` shape below).
- **`src/Game.Main/InitSystem.cs`**: extend `CreateCountryPopulationEntities` (or a sibling method called at the same point) to also create `Resource{ResourceId="country_score", Value=0}` + one `Instant` + one `Monthly` `ResourceEffect`, both linked via `ResourceLink("country_score")`, both carrying `ResourceCollector { CollectorId = CountryScoreCollector.Id }`.
- **`src/Game.Systems/CountryScoreSystem.cs`**: delete `Update`/`Recompute` entirely (their logic now lives in `CountryScoreCollector`, driven generically by `ResourceSystem`). Keep only:
  ```csharp
  public static double GetScore(IReadOnlyWorld world, string countryId) {
      int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
      foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
          ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
          Resource[] resources = arch.GetColumn<Resource>();
          int count = arch.Count;
          for (int i = 0; i < count; i++) {
              if (owners[i].OwnerId == countryId && resources[i].ResourceId == "country_score") {
                  return resources[i].Value;
              }
          }
      }
      return 0;
  }
  ```
- **`src/Game.Main/GameLogic.cs`**: remove the `CountryScoreSystem.Update(_world, _previousTime, currentTime, _countryScoreCoefficient);` call (line 95 today) and the `_countryScoreCoefficient` field (superseded by the collector registry).
- **`src/Game.Main/GameLogic.cs`**, `LoadState`: remove the `CountryScoreSystem.Recompute(_world, _countryScoreCoefficient);` call — `country_score` is now an ordinary `[Savable]` `Resource`, correct immediately from the loaded snapshot. **Keep** `OrgScoreSystem.Recompute(_world);` on the line below, unchanged (org score is not migrated, see step 7).
- **`src/Game.Main/InitSystem.cs`**: remove the `CountryScoreSystem.Recompute(world, settings.CountryScoreCoefficient);` call near the end of `Run` — superseded by the `Instant` effect created in step 5, which fires automatically on the first `ResourceSystem.Update`. **Keep** `OrgScoreSystem.Recompute(world);` immediately below it, unchanged.

### 7. `OrgScoreSystem` read-path update

- **`src/Game.Systems/OrgScoreSystem.cs`**, `Recompute`: replace the block that builds `scoreByCountryId` by iterating `Country + Score` archetypes (lines 26–35 today) with a loop over `Country` entities calling `CountryScoreSystem.GetScore(world, countryId)` per country, populating the same local `Dictionary<string, double> scoreByCountryId`. The rest of `Recompute` (control aggregation, per-org total, composing `Score` onto `Organization`) is completely unchanged — `Organization`'s own `Score` component, daily cadence, non-`[Savable]`, stays exactly as today.

### 8. Config

- **`src/Game.Configs/GameSettings.cs`**: add `public string[] ResourceIdUpdateOrder { get; set; } = { "population", "country_population", "country_score" };`. Keep `PopulationGrowthPercentPerMonth`/`CountryScoreCoefficient` fields as-is — they now feed `ResourceCollectorRegistry.CreateDefault(...)` instead of the deleted systems' `Update` calls.
- **`Assets/Configs/game_settings.json`**: add `"resourceIdUpdateOrder": ["population", "country_population", "country_score"]`.

### 9. `GameLogic` wiring

- Constructor: add `readonly ResourceCollectorRegistry _resourceCollectorRegistry;` and `readonly string[] _resourceIdUpdateOrder;`, set via `_resourceCollectorRegistry = ResourceCollectorRegistry.CreateDefault(settings.PopulationGrowthPercentPerMonth, settings.CountryScoreCoefficient);` and `_resourceIdUpdateOrder = settings.ResourceIdUpdateOrder;`, alongside the existing `_speedMultipliers` assignment. Remove `_populationGrowthPercent`/`_countryScoreCoefficient` fields (step 4/6).
- `Update(float deltaTime)`: change the existing `ResourceSystem.Update(_world, _previousTime, currentTime);` call to `ResourceSystem.Update(_world, _previousTime, currentTime, _resourceCollectorRegistry, _resourceIdUpdateOrder);`. Remove the now-redundant `ProvincePopulationGrowthSystem.Update(...)` and `CountryScoreSystem.Update(...)` call lines entirely — both are superseded by the single `ResourceSystem.Update` call now handling all three resourceIds in order.

### 10. Documentation

- **`.claude/rules/unity/ecs_patterns.md`**: in the "Composition over parallel lookup entities" section, add a note after the existing `Country + Score` / `Organization + Score` example clarifying that `country_score` moved to the generic `Resource`/`ResourceOwner` shape as of this feature (for pipeline uniformity with `population`/`gold`/`recruits`), while `Organization + Score` remains a valid, unchanged example of the same composition pattern.

## Steps

### Agent Steps

- [ ] **Add `IResourceCollector`** — `src/Game.Systems/IResourceCollector.cs`, per Approach §1.
- [ ] **Add `ResourceCollectorRegistry`** — `src/Game.Systems/ResourceCollectorRegistry.cs`, per Approach §1 (without the three `Register` calls yet — those are added once the collector classes exist in the next step).
- [ ] **Add `ResourceCollector` component** — `src/Game.Components/ResourceCollector.cs`, per Approach §1.
- [ ] **Add the three collectors** — `src/Game.Systems/PopulationGrowthCollector.cs`, `src/Game.Systems/CountryPopulationCollector.cs`, `src/Game.Systems/CountryScoreCollector.cs`, per Approach §2; wire all three into `ResourceCollectorRegistry.CreateDefault`.
- [ ] **Restructure `ResourceSystem.Update`** — `src/Game.Systems/ResourceSystem.cs`, per Approach §3: add the optional `collectorRegistry`/`resourceIdUpdateOrder` parameters, the per-resourceId resolve-then-apply loop, and the unordered fallback pass for everything else. Verify the no-args-supplied path is behaviorally identical to today (this is what keeps all existing tests green unmodified).
- [ ] **Migrate province population growth** — extend `CreateProvincePopulationEntities` in `src/Game.Main/InitSystem.cs` per Approach §4; delete `src/Game.Systems/ProvincePopulationGrowthSystem.cs` and `src/Game.Tests/ProvincePopulationGrowthSystemTests.cs`; remove its call and the `_populationGrowthPercent` field from `src/Game.Main/GameLogic.cs`.
- [ ] **Add `country_population`** — new `CreateCountryPopulationEntities` in `src/Game.Main/InitSystem.cs` per Approach §5.
- [ ] **Migrate `country_score`** — extend the same init method per Approach §6; trim `src/Game.Systems/CountryScoreSystem.cs` down to `GetScore` only; remove its `Update`/`Recompute` call sites and the `_countryScoreCoefficient` field from `src/Game.Main/GameLogic.cs` (`Update` and `LoadState`) and from `src/Game.Main/InitSystem.cs`.
- [ ] **Update `OrgScoreSystem`'s read path** — per Approach §7.
- [ ] **Add config** — `ResourceIdUpdateOrder` in `src/Game.Configs/GameSettings.cs`; `resourceIdUpdateOrder` in `Assets/Configs/game_settings.json`; per Approach §8.
- [ ] **Wire `GameLogic`** — constructor + `Update` per Approach §9.
- [ ] **Update `ecs_patterns.md`** — per Approach §10.
- [ ] **Add/extend tests** — per the Tests section below.
- [ ] **Rebuild the Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release` so `Assets/Plugins/Core/` picks up every new/changed type.

### User Steps

### 1. Confirm a clean Unity import

After the DLL rebuild, let Unity finish its domain reload and check `read_console(types=["error"])` — this feature touches no Unity-side script; the only expected effect is updated `Assets/Plugins/Core/*.dll` files and the new `resourceIdUpdateOrder` key in `Assets/Configs/game_settings.json` being picked up cleanly.

### 2. Sanity-check initial values in Play mode

Enter Play mode. Confirm every available country's `country_population` and `country_score` (via `CountryScoreSystem.GetScore` or a temporary debug read) are non-zero immediately at tick one — proving the `Instant`-effect seeding path works without waiting for the first month boundary.

### 3. Verify province population growth still compounds correctly

Advance time across a month boundary and confirm province `population` grows by the same compounding percentage as before this change (spot-check one province's value against the pre-change formula).

### 4. Verify country score still reacts to ownership changes

Trigger `DebugChangeProvinceOwnerCommand` mid-month, confirm no immediate score change, then advance past the next month boundary and confirm the province's population now counts toward its new owner's `country_score`.

### 5. Verify save/load

Save, reload, and confirm `country_population`/`country_score` read correct values immediately (not `0`, not stale) with no forced recompute needed.

## Tests

Test project: `src/Game.Tests/` (xUnit, snake_case names, no explicit access modifier on `[Fact]` methods, matching existing files).

- **New `src/Game.Tests/ResourceCollectorRegistryTests.cs`:**
  - `resolve_returns_registered_collector`
  - `resolve_throws_for_unknown_collector_id`

- **Extend `src/Game.Tests/ResourceSystemTests.cs`:**
  - `collector_tagged_effect_value_recomputed_before_apply` — a resource + a `Monthly` effect carrying `ResourceCollector`, a stub `IResourceCollector` returning a fixed delta based on `currentValue`, registered under a test id → after `Update(world, Jan31, Feb1, registry, new[] { resourceId })`, the resource reflects the stub's computed delta, not whatever static `Value` the effect was created with.
  - `resourceid_update_order_resolves_dependency_before_dependent` — two resourceIds `"a"` then `"b"`, `"a"`'s collector adds a fixed amount, `"b"`'s collector reads `"a"`'s *current* resource value (via a stub collector capturing `world`) → confirms `"b"`'s collector observes `"a"`'s already-applied value within the same `Update` call, not the pre-call value.
  - `resourceids_not_in_order_list_process_unaffected` — gold-style static effect with no `ResourceCollector`, resourceId absent from `resourceIdUpdateOrder` → applies exactly as today's existing tests already assert (regression guard that the ordered path doesn't interfere with the fallback pass).
  - `null_registry_and_order_preserve_legacy_behavior` — call `Update(world, previousTime, currentTime)` with no new params (exactly today's existing test call shape) → byte-identical result to pre-change behavior (this is effectively re-asserting the existing `ResourceSystemTests`/`ResourceEffectMaxTotalTests` cases still pass unmodified, but as an explicit regression test for the compatibility shim itself).

- **New `src/Game.Tests/PopulationGrowthCollectorTests.cs`:**
  - `compute_returns_percent_of_current_value` — `new PopulationGrowthCollector(0.075).Compute("prov_a", 1000.0, world) == 0.75` (`1000 * 0.075 / 100`).

- **New `src/Game.Tests/CountryPopulationCollectorTests.cs`:**
  - `compute_sums_population_of_owned_provinces` — two provinces owned by the same country with population `Resource`s → delta equals their sum minus `currentValue`.
  - `compute_reads_current_runtime_owner_not_seed_country_id` — mirrors `CountryScoreSystemTests`'s existing equivalent: change ownership via `ProvinceOwnershipSystem.ChangeOwner`, confirm the aggregate follows the new owner.
  - `compute_returns_negative_delta_for_zero_owned_provinces` — country with no owned provinces and a nonzero `currentValue` → delta drives it back to `0` (`0 - currentValue`).

- **New `src/Game.Tests/CountryScoreCollectorTests.cs`:**
  - `compute_returns_coefficient_times_country_population` — `country_population` resource seeded, collector with a known coefficient → delta lands `country_score` at `coefficient * population`.

- **Extend `src/Game.Tests/InitSystemTests.cs`:**
  - `country_population_and_score_seeded_at_init_from_province_population` — after the first `GameLogic.Update`, every available country's `country_population`/`CountryScoreSystem.GetScore` reflect the seed population/coefficient, non-zero from tick one (mirrors the deleted `CountryScoreSystemTests`' init coverage, now exercised through the full pipeline).
  - `country_score_correct_immediately_after_load_with_no_forced_recompute` — mirrors the existing `country_score_recomputed_immediately_after_load`-style test, but now asserting the value is correct purely because it was persisted (no `Recompute` call exists anymore to rely on) — save, reload via `LoadState`, assert `CountryScoreSystem.GetScore` matches the pre-save value exactly (not merely "non-zero").
  - `province_population_growth_still_compounds_monthly` — replaces the deleted `ProvincePopulationGrowthSystemTests.cs` coverage: seed a province, advance across two month boundaries via `GameLogic.Update`, assert the compounding result matches the pre-migration formula.

- **Extend `src/Game.Tests/OrgScoreSystemTests.cs`** (or wherever org-score coverage currently lives — confirm exact file via search before editing): assert `OrgScoreSystem.Recompute` still produces correct totals when `country_score` is a `Resource` rather than a `Score` component — this is the one behavioral seam this migration must not silently break, since `Organization`'s own `Score` composition is untouched but its *input* data's storage changed underneath it.

- **Delete `src/Game.Tests/ProvincePopulationGrowthSystemTests.cs` and `CountryScoreSystemTests.cs`** — their `Update`/`Recompute`-level coverage is superseded by the collector-level tests above plus `ResourceSystemTests`' new ordered-pipeline tests; anything not already covered (e.g. `GetScore` returning `0` for an unknown country) is re-added as a one-line case in the extended `InitSystemTests.cs` or a small `CountryScoreSystemTests.cs` retained *only* for `GetScore` coverage if deletion turns out to drop that case — confirm coverage parity during implementation rather than deleting blind.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* Every new/changed type (`IResourceCollector`, `ResourceCollectorRegistry`, `ResourceCollector`, the three collectors, `ResourceSystem`, `InitSystem`, `GameLogic`, `CountryScoreSystem`, `OrgScoreSystem`, `GameSettings`) lives in `src/Game.Systems`, `src/Game.Components`, `src/Game.Main`, `src/Game.Configs` — no MonoBehaviour, no `Assets/Scripts/Unity/*` change.
- *VContainer sole DI.* No new registrations — `ResourceCollectorRegistry` is constructed directly inside `GameLogic`'s constructor, matching how `BotFeatureRegistry.CreateDefault(...)` is already constructed directly (plain C#, not Unity-side) rather than through the container.
- *UI Toolkit only.* No UI surface added or modified.
- *URP only.* No rendering/shader/material change.
- *One `.asmdef` per feature folder.* Not applicable — this feature only touches `src/` (`.csproj`-based), no `Assets/Scripts/` change.
- *Planning/Specification discipline.* Follows an approved spec (`Docs/Specs/26_07_18_17_resource-collector-pipeline/spec.md`) via the standard `/specify` → `/plan` sequence.
- *File organisation.* Plan lives at `Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md`, matching its spec's directory.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching all surrounding files referenced in this plan (`ResourceSystem.cs`, `CountryScoreSystem.cs`, `BotFeatureRegistry.cs`).

Use /implement to start working on the plan or request changes.
