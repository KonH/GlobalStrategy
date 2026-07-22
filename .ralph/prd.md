# Ralph PRD — VisualState update optimization

Make every in-scope `VisualState`/`ResourcesState`/`TimeState` class's `Set(...)` a no-op (no `PropertyChanged` fire) when called with logically-unchanged data, using structural (not reference) equality for list/dictionary/set-shaped fields, while leaving call sites, event-arg shape, and `AnimatableInt`/`AnimatableDouble`'s own animation logic untouched — proven via dedicated BenchmarkDotNet benchmarks showing the added equality-check cost is minimal on the no-op path. Source: [approved spec and plan](../Docs/Specs/26_07_21_08_visualstate-update-optimization/).

## How this file works

- The loop implements the first task with `"passes": false`, verifies it via its `gate`, flips the flag, commits, and repeats.
- Tasks must be **atomic** (one logical change), **verifiable** (the `gate` decides pass/fail — a shell command, or a Unity MCP check: `refresh_unity` + empty error console), and **ordered** (dependencies first).
- When every task has `"passes": true`, the loop stops.

## Tasks

```json
[
	{
		"category": "state-equality-helper",
		"description": "Add the centralized StateEquality helper class with list/dictionary comparers and per-entry-type comparer functions.",
		"steps": [
			"Create src/Game.Main/StateEquality.cs with ListEquals<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> elementEquals) and DictionaryContentEquals<TValue>(IReadOnlyDictionary<string, TValue> a, IReadOnlyDictionary<string, TValue> b).",
			"Add named per-entry-type comparer functions for OrgControlEntry, SkillEntry, CharacterStateEntry, OrgCharacterSlotEntry, OrgCountryEntry, ActionCardEntry, VisualResourceChangeEffect, LeaderboardEntryState, GameLogEntry, ResourceStateEntry, ControlIncomeEntry, EffectStateEntry.",
			"Compare AnimatableInt/AnimatableDouble fields (CharacterStateEntry.Opinion, ResourceStateEntry.Value) by their .Actual value, never by reference or .Display.",
			"CharacterStateEntryEquals must be reusable both directly by CountryCharactersState and nested inside OrgCharacterSlotEntry comparison for OrgCharactersState."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "visualstate-scalar",
		"description": "Add early-return-on-equal to Set(...) for scalar-field state classes.",
		"steps": [
			"Update Set(...) in SelectedCountryState, SelectedOrganizationState, SelectedProvinceState, PlayerOrganizationState (src/Game.Main/VisualState.cs) and TimeState (src/Game.Main/TimeState.cs) to compare all scalar arguments against stored values and return before assignment/PropertyChanged when all are equal.",
			"Follow the existing LocaleState/MapLensState pattern exactly: if (equal) { return; } before any assignment; assign fields and fire PropertyChanged only in the changed path.",
			"Do not modify LocaleState or MapLensState themselves."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "visualstate-list",
		"description": "Add structural-equality early-return to Set(...) for list-holding state classes, preserving unconditional SetActual calls.",
		"steps": [
			"Update CountryControlState.Set: capture bool usedChanged = UsedControl.Actual != used BEFORE calling UsedControl.SetActual(used); call SetActual unconditionally next; combine usedChanged with StateEquality.ListEquals(...) over OrgEntries (using OrgControlEntryEquals) to decide whether PropertyChanged fires.",
			"Update CountryCharactersState.Set, OrgCharactersState.Set, OrgMapState.Set, OrgActionsState.Set, CountryActionsState.Set (Hand and Deck), LeaderboardState.Set (Organizations and Countries), GameLogState.Set, VisualEffectCollection.Set (Effects) using StateEquality.ListEquals with the matching per-entry comparer; early-return before assignment when all lists are equal.",
			"Update CountryResourcesState.Set using StateEquality.ListEquals over ResourceStateEntry/ControlIncomeEntry lists; no SetActual capture needed here since AnimatableDouble.SetActual happens in VisualStateConverter.BuildResources, outside this Set method."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "visualstate-dictionary-set",
		"description": "Add durable-content-only equality early-return to Set(...) for dictionary- and HashSet-holding state classes, excluding transient recent-event fields.",
		"steps": [
			"Update ProvinceOwnershipState.Set and ProvinceOccupationState.Set and CountryScoreState.Set using StateEquality.DictionaryContentEquals over the durable dictionary only (OwnerByProvinceId / OccupierByProvinceId / ScoreByCountryId); Recent* transient parameters are read/assigned as today but never enter the equality check.",
			"Update DiscoveredCountriesState.Set using CountryIds.SetEquals(ids); RecentlyDiscovered is excluded from the equality check the same way."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "call-site-review",
		"description": "Re-confirm transient-field call-site safety against current main before finalizing equality checks.",
		"steps": [
			"Re-read GameLogic.cs's ProvinceOwnership.Set/ProvinceOccupation.Set call sites and confirm both are still guarded by if (changed) { ... } so a Recent* field is only set in the same call where durable dictionary content also changes.",
			"Re-read VisualStateConverter.UpdateProvinceOwnership/UpdateProvinceOccupation and confirm they still pass the previous Recent* values straight through unchanged.",
			"Re-read VisualStateConverter.UpdateDiscoveredCountries and confirm RecentlyDiscovered still only takes a new value in the same call where CountryIds gains a member.",
			"If any call site has drifted such that a transient field is set without the durable data also changing in the same call, stop and surface it instead of silently proceeding with the equality-check change."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "benchmarks",
		"description": "Add ScalarVisualStateSetBenchmarks covering scalar-field state classes including the LocaleState/MapLensState reference implementations.",
		"steps": [
			"Create src/Game.Benchmarks/ScalarVisualStateSetBenchmarks.cs, [MemoryDiagnoser], following VisualStateConverterBenchmarks.cs's GameWorldFixture.Build() + one warm Update(...) pass fixture-construction convention.",
			"Add a <ClassName>_NoOp/<ClassName>_Update [Benchmark] method pair for SelectedCountryState, SelectedOrganizationState, SelectedProvinceState, PlayerOrganizationState, TimeState, LocaleState, MapLensState.",
			"NoOp calls Set(...) with the exact values currently stored; Update calls Set(...) with a genuinely different value."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "benchmarks",
		"description": "Add ListVisualStateSetBenchmarks covering list-holding state classes with realistic collection sizes.",
		"steps": [
			"Create src/Game.Benchmarks/ListVisualStateSetBenchmarks.cs, [MemoryDiagnoser], following the same GameWorldFixture-based convention.",
			"Add a <ClassName>_NoOp/<ClassName>_Update [Benchmark] method pair for CountryControlState, CountryCharactersState, OrgCharactersState, OrgMapState, OrgActionsState, CountryActionsState, LeaderboardState, GameLogState, CountryResourcesState, VisualEffectCollection.",
			"Use the harness's existing ~163-country fixture volume for list sizes, not empty/single-element lists; Update variants append or mutate one entry on a copy of the stored collection."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "benchmarks",
		"description": "Add DictionaryAndSetVisualStateSetBenchmarks covering dictionary- and HashSet-holding state classes.",
		"steps": [
			"Create src/Game.Benchmarks/DictionaryAndSetVisualStateSetBenchmarks.cs, [MemoryDiagnoser], following the same GameWorldFixture-based convention.",
			"Add a <ClassName>_NoOp/<ClassName>_Update [Benchmark] method pair for ProvinceOwnershipState, ProvinceOccupationState, CountryScoreState, DiscoveredCountriesState.",
			"Use realistic dictionary/set sizes matching production volume; Update variants change one key/value or one set member on a copy of the stored collection."
		],
		"gate": "dotnet build src/GlobalStrategy.Core.sln -c Release",
		"passes": false
	},
	{
		"category": "tests",
		"description": "Add VisualStateChangeNotificationTests covering scalar, list, dictionary, and HashSet equality-check shapes.",
		"steps": [
			"Create src/Game.Tests/VisualStateChangeNotificationTests.cs, instantiating state classes directly (no GameLogic/ECS World needed).",
			"TimeState test: Set(t, false, 0) twice with identical arguments fires PropertyChanged 0 times on the second call; Set(t, true, 0) fires exactly once more.",
			"CountryControlState test: two field-equal-but-reference-distinct List<OrgControlEntry> instances passed to consecutive Set(used, ...) calls with the same used fire PropertyChanged 0 additional times, but UsedControl.Actual is still correctly used and UsedControl.PropertyChanged still fired from the SetActual call (subscribe separately); a subsequent Set(differentUsed, ...) fires CountryControlState.PropertyChanged once.",
			"CountryScoreState test: two separately-constructed Dictionary<string,double> instances with same keys/values in different insertion order fire 0 additional times; a value or key difference fires once.",
			"ProvinceOwnershipState test: same durable OwnerByProvinceId content across two Set(...) calls but different recentProvinceId/recentOldOwnerId/recentNewOwnerId arguments still fires 0 additional times.",
			"DiscoveredCountriesState test: two HashSet<string> instances with same members in different insertion order fire 0 additional times; a member added/removed fires once; a different recentlyDiscovered string alongside an unchanged CountryIds set still fires 0 additional times."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "verification",
		"description": "Run the full core test suite to confirm no regressions from the Set(...) equality-check changes.",
		"steps": [
			"Run dotnet test src/GlobalStrategy.Core.sln and confirm all existing and new tests pass.",
			"Confirm no test that reads VisualState sub-state values regressed, since assigned values are unchanged and only the fire/don't-fire decision changed."
		],
		"gate": "dotnet test src/GlobalStrategy.Core.sln",
		"passes": false
	},
	{
		"category": "benchmark-baseline",
		"description": "Establish the new benchmark baseline and confirm the existing regression gate passes.",
		"steps": [
			"Run the dotnet-benchmark skill with --update-baseline once, since the new <ClassName>_NoOp/<ClassName>_Update methods have no prior baseline entry to compare against.",
			"Run the dotnet-benchmark skill with --compare to confirm the harness's existing 5%-regression gate passes cleanly on a second run.",
			"Confirm no existing (non-VisualState) benchmark regressed from the Set(...) changes, e.g. VisualStateConverterBenchmarks.Update."
		],
		"gate": "dotnet-benchmark skill --compare",
		"passes": false
	}
]
```
