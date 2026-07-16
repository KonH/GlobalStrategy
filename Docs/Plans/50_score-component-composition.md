# Plan: Score Component Composition Refactor

## Goal

Refactor the already-merged, already-shipped `CountryScore`/`CountryScoreSystem` (PR #16, on `main`) to use **component composition** instead of a parallel id-keyed entity: replace the standalone `CountryScore { CountryId, Value }` component/entity with a shared, generic `Score { Value }` component attached directly onto the existing `Country` entity. This is the pattern just recorded in `.claude/rules/unity/ecs_patterns.md` under "Composition over parallel lookup entities for derived per-entity state," decided after reviewing the merged code.

This plan is purely technical (no behavior change, no new acceptance criteria) — per `Docs/Constitution.md`'s Specification Discipline principle, it skips `/specify` and goes straight to `Docs/Plans/`.

This plan is a **prerequisite** for `Docs/Specs/49_org-scoring/plan.md`, which is being rewritten in parallel to define `OrgScoreSystem` using the same shared `Score` component (composed onto `Organization` entities). Landing this refactor first means org-scoring depends on one shared `Score` component instead of each score system inventing its own copy of the same struct.

## Approach

**Grep confirms exactly 8 files touch the `CountryScore` component/system** (a 9th, `GameSettings.cs`, only has an unrelated `CountryScoreCoefficient` config property — not the component — and needs no change):

1. `src/Game.Components/CountryScore.cs`
2. `src/Game.Systems/CountryScoreSystem.cs`
3. `src/Game.Main/VisualStateConverter.cs`
4. `src/Game.Main/InitSystem.cs` (only calls `CountryScoreSystem.Recompute(...)` — public API, unaffected)
5. `src/Game.Main/GameLogic.cs` (only calls `CountryScoreSystem.Update(...)`/`.Recompute(...)` — public API, unaffected)
6. `src/Game.Main/VisualState.cs` (`CountryScoreState`/`VisualState.CountryScore` — output DTO, decoupled from internal ECS shape, unaffected)
7. `src/Game.Tests/CountryScoreSystemTests.cs` (exercises only `CountryScoreSystem.Update`/`.Recompute`/`.GetScore` — the public API — never references the `CountryScore` type directly)
8. `src/Game.Tests/InitSystemTests.cs` (same — only calls `CountryScoreSystem.GetScore(...)`)

Files 4–8 need **zero logic changes** — they only touch `CountryScoreSystem`'s public API (`Update`/`Recompute`/`GetScore`), which keeps its exact signature. Only files 1–3 change internally.

### 1. New shared component — `src/Game.Components/Score.cs`

```csharp
namespace GS.Game.Components {
	// Not [Savable] — see the [Savable] omission pattern above.
	public struct Score {
		public double Value;
	}
}
```

### 2. Delete `src/Game.Components/CountryScore.cs`

No more per-domain score component — `Country + Score` composition replaces it.

### 3. Rewrite `src/Game.Systems/CountryScoreSystem.cs`

Current `Recompute` does three separate passes: (a) build `populationByProvinceId`, (b) build `scoreEntityByCountryId` by scanning existing `CountryScore` entities, (c) build `countryIds` by scanning `Country` entities, then a fourth loop over `countryIds` that looks up `scoreEntityByCountryId` to decide mutate-vs-create. The composition refactor **eliminates pass (b) and merges (c) with the final loop** — the entity carrying `Country` *is* the entity that gets `Score` attached, so there is no separate lookup to build:

```csharp
public static class CountryScoreSystem {
	public static void Update(World world, DateTime previousTime, DateTime currentTime, double coefficient) {
		bool isMonthBoundary = previousTime.Month != currentTime.Month
			|| previousTime.Year != currentTime.Year;
		if (!isMonthBoundary) {
			return;
		}
		Recompute(world, coefficient);
	}

	public static void Recompute(World world, double coefficient) {
		var provincesByOwner = ProvinceOwnershipSystem.GetProvincesByOwner(world);

		var populationByProvinceId = new Dictionary<string, double>();
		int[] resourceRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
		foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
			ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
			Resource[] resources = arch.GetColumn<Resource>();
			int count = arch.Count;
			for (int i = 0; i < count; i++) {
				if (owners[i].OwnerType != OwnerType.Province
					|| resources[i].ResourceId != ProvincePopulationGrowthSystem.PopulationResourceId) {
					continue;
				}
				populationByProvinceId[owners[i].OwnerId] = resources[i].Value;
			}
		}

		// Single pass over Country entities — compute + attach/update Score directly,
		// no separate CountryId -> scoreEntity lookup needed (see ecs_patterns.md).
		int[] countryRequired = { TypeId<Country>.Value };
		foreach (Archetype arch in world.GetMatchingArchetypes(countryRequired, null)) {
			Country[] countries = arch.GetColumn<Country>();
			int count = arch.Count;
			for (int i = 0; i < count; i++) {
				string countryId = countries[i].CountryId;
				double totalPopulation = 0;
				if (provincesByOwner.TryGetValue(countryId, out var provinceIds)) {
					foreach (var provinceId in provinceIds) {
						if (populationByProvinceId.TryGetValue(provinceId, out double population)) {
							totalPopulation += population;
						}
					}
				}

				double value = coefficient * totalPopulation;
				int entity = arch.Entities[i];
				if (world.Has<Score>(entity)) {
					world.Get<Score>(entity).Value = value;
				} else {
					world.Add(entity, new Score { Value = value });
				}
			}
		}
	}

	public static double GetScore(IReadOnlyWorld world, string countryId) {
		int[] required = { TypeId<Country>.Value, TypeId<Score>.Value };
		foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
			Country[] countries = arch.GetColumn<Country>();
			Score[] scores = arch.GetColumn<Score>();
			int count = arch.Count;
			for (int i = 0; i < count; i++) {
				if (countries[i].CountryId == countryId) {
					return scores[i].Value;
				}
			}
		}
		return 0;
	}
}
```

Note on the `foreach` + in-loop `world.Add`/`world.Get` pattern: adding a *new* component type to an entity already being iterated via `GetMatchingArchetypes` is the same operation `InitSystem.DiscoverInitialCountries` performs, but that code collects entity IDs into a list first and adds components in a second loop, with a comment explaining why: "calling `world.Add` inside `GetMatchingArchetypes` would create new archetypes and mutate the dictionary mid-iteration, throwing `InvalidOperationException`." **This same hazard applies here on an entity's *first* `Recompute` call** (when it doesn't have `Score` yet — `world.Add` triggers an archetype move for that entity). The implementer must collect `(entity, countryId)` pairs into a `List<(int, string)>` during the archetype scan, then do the has/mutate/add branch in a second loop over that list — not inline inside the `foreach (Archetype arch in ...)` loop as drafted above for readability. **Update the code above accordingly before implementing** — this is a correctness-critical deviation from the snippet, called out explicitly so it isn't missed.

### 4. Rewrite `UpdateCountryScore` in `src/Game.Main/VisualStateConverter.cs`

Current (lines ~626–636):
```csharp
void UpdateCountryScore(IReadOnlyWorld world) {
	var scoreByCountryId = new Dictionary<string, double>();
	int[] required = { TypeId<CountryScore>.Value };
	foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
		CountryScore[] scores = arch.GetColumn<CountryScore>();
		int count = arch.Count;
		for (int i = 0; i < count; i++) {
			scoreByCountryId[scores[i].CountryId] = scores[i].Value;
		}
	}
	_state.CountryScore.Set(scoreByCountryId);
}
```

New:
```csharp
void UpdateCountryScore(IReadOnlyWorld world) {
	var scoreByCountryId = new Dictionary<string, double>();
	int[] required = { TypeId<Country>.Value, TypeId<Score>.Value };
	foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
		Country[] countries = arch.GetColumn<Country>();
		Score[] scores = arch.GetColumn<Score>();
		int count = arch.Count;
		for (int i = 0; i < count; i++) {
			scoreByCountryId[countries[i].CountryId] = scores[i].Value;
		}
	}
	_state.CountryScore.Set(scoreByCountryId);
}
```

`VisualState.CountryScoreState`/`VisualState.CountryScore` itself is unchanged — it's an output DTO (`IReadOnlyDictionary<string, double> ScoreByCountryId`) that doesn't know or care how the ECS side stores the value.

### 5. Everything else — no changes

`InitSystem.Run`'s `CountryScoreSystem.Recompute(world, settings.CountryScoreCoefficient);` call, `GameLogic`'s constructor/`Update`/`LoadState` calls, `GameSettings.CountryScoreCoefficient`, `game_settings.json`'s `countryScoreCoefficient` key, and both test files all go through the unchanged public API and require no edits.

## Steps

### Agent Steps

- [ ] **Add the shared `Score` component** — `src/Game.Components/Score.cs` as shown above.
- [ ] **Delete `CountryScore.cs`** — `src/Game.Components/CountryScore.cs`.
- [ ] **Rewrite `CountryScoreSystem.Recompute`/`GetScore`** — `src/Game.Systems/CountryScoreSystem.cs`, using the two-pass (collect-then-mutate) form described in the correctness note above, not the naive inline form.
- [ ] **Rewrite `UpdateCountryScore`** — `src/Game.Main/VisualStateConverter.cs`.
- [ ] **Grep-confirm no other `CountryScore` references remain** — `grep -rn "CountryScore" src/` should return zero hits after this refactor (the `CountryScoreSystem`/`CountryScoreState`/`CountryScoreCoefficient` names are expected and correct to keep — only the `CountryScore` *component* type name should disappear; re-run the grep and sanity-check every remaining hit is one of those three, not the deleted struct).
- [ ] **Run the test suite** — `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`) — expect `CountryScoreSystemTests.cs` and the `InitSystemTests.cs` score-related facts to pass with no assertion changes.
- [ ] **Rebuild the Core DLLs** — `dotnet build src/GlobalStrategy.Core.sln -c Release`.

### User Steps

1. **Confirm a clean Unity import** — check `read_console(types=["error"])` after the DLL rebuild.
2. **Sanity-check scores still populate in Play mode** — via `VisualState.CountryScore.ScoreByCountryId` (no consumer UI exists yet, per spec 47's scope — confirm through a debugger/log or a temporary breakpoint) that country scores are still non-zero and correct after this refactor, matching pre-refactor behavior.
3. **Verify save/load still recomputes correctly** — save, reload, confirm scores are immediately correct (this exercises `GameLogic.LoadState`'s forced `Recompute` call against the new composed-entity storage).

## Tests

`src/Game.Tests/CountryScoreSystemTests.cs` exercises only the public API (`CountryScoreSystem.Update`/`.Recompute`/`.GetScore`) and needs **no assertion changes** — every existing fact (`score_computed_from_owned_province_population_at_month_boundary`, `country_with_zero_owned_provinces_has_zero_score`, `score_unchanged_within_same_month`, `ownership_change_mid_month_does_not_affect_score_until_boundary`, `multiple_months_skipped_recomputes_once_from_current_state`, `recompute_reads_current_runtime_owner_not_seed_country_id`, `recompute_is_forced_and_ungated`, `get_score_returns_zero_for_unknown_country`) should pass unmodified against the refactored internals. Likewise `InitSystemTests.cs`'s two score-related assertions (lines ~186–187, ~207–208) need no changes.

**One new fact worth adding** to `CountryScoreSystemTests.cs`: `score_is_composed_onto_the_country_entity_not_a_separate_entity` — after `Recompute`, assert that the entity found via a `Country`-only query for a given `countryId` is the *same* entity id returned by a `Country + Score`-required query for that same `countryId` (i.e. there is exactly one entity carrying both components, not two). This directly asserts the composition property the refactor exists to establish, which none of the existing API-level tests (which only ever call `GetScore`) would catch if a future regression reintroduced a parallel entity.

Run: `dotnet test src/GlobalStrategy.Core.sln` (`dangerouslyDisableSandbox: true`).

## Constitution Check

Checked against `Docs/Constitution.md`.

**No conflicts found.**

- *ECS for all game logic in `src/`.* `Score` (component) and the refactored `CountryScoreSystem` live in `src/Game.Components`/`src/Game.Systems` — no MonoBehaviour, no Unity-side logic.
- *VContainer sole DI.* Not applicable — no new registrations, no Unity-side consumer touched.
- *UI Toolkit only.* Not applicable — no UI surface added or modified.
- *URP only.* Not applicable — no rendering change.
- *One `.asmdef` per feature folder.* Not applicable — scoped to `src/` (`.csproj`-based).
- *Planning/Specification discipline.* This is a purely technical refactor of already-shipped code with no new user-facing behavior or acceptance criteria — Constitution explicitly permits skipping `/specify` for such tasks ("purely technical tasks (migrations, refactors, infra) may skip the spec and go straight to `/plan`").
- *File organisation.* Plan lives at `Docs/Plans/50_score-component-composition.md` — correct index (next after `Docs/Specs/49_org-scoring/`, shared index space), correct directory for a spec-less technical plan.
- *C# style.* Tabs, braces always, `_`-prefixed private members, no redundant access modifiers — matching the surrounding files this plan edits.

Use /implement to start working on the plan or request changes.
