# Benchmark Summary

> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container class of machine) - no cross-machine normalization is attempted.

Mode: `compare`
Timestamp: 2026-07-23 20:23:44 UTC
Overall: FAIL

| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |
|---|---|---|---|---|---|
| ControlSystemBenchmarks.Update | 61122.8 | 29181.4 | -52.3% | pass | 1752 |
| CountryPopulationCollectorBenchmarks.Compute | 422477.0 | 269819.5 | -36.1% | pass | 608906 |
| CountryScoreCollectorBenchmarks.Compute | 5204.0 | 5012.0 | -3.7% | pass | 128 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_NoOp | 228372.0 | 237540.5 | +4.0% | pass | 163709 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_Update | 10.4 | 10.5 | +0.9% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_NoOp | 24.5 | 24.8 | +1.3% | pass | 80 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_Update | 10.9 | 10.9 | +0.2% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_NoOp | 471.5 | 460.7 | -2.3% | pass | 832 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_Update | 2.9 | 2.9 | -0.4% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_NoOp | 84.8 | 83.1 | -2.1% | pass | 176 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_Update | 9.6 | 10.9 | +13.6% | FAIL | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=1000) | 8483.4 | 8176.7 | -3.6% | pass | 37880 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=1000) | 64299.4 | 145327.6 | +126.0% | FAIL | 136000 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=1000) | 1304.8 | 2752.2 | +110.9% | FAIL | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=1000) | 3259.9 | 6895.2 | +111.5% | FAIL | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=10000) | 126469.1 | 259422.0 | +105.1% | FAIL | 652642 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=10000) | 719299.7 | 1506964.0 | +109.5% | FAIL | 1818627 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=10000) | 12467.7 | 26991.8 | +116.5% | FAIL | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=10000) | 33419.2 | 68956.1 | +106.3% | FAIL | 0 |
| FullTickBenchmarks.Tick_RegularDay | 1570900.0 | 1587595.8 | +1.1% | pass | 132056 |
| FullTickBenchmarks.Tick_MonthBoundary | 65362562.2 | 62412021.4 | -4.5% | pass | 14088208 |
| ListVisualStateSetBenchmarks.CountryControlState_NoOp | 14.6 | 24.8 | +70.1% | FAIL | 32 |
| ListVisualStateSetBenchmarks.CountryControlState_Update | 8.0 | 7.7 | -3.3% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryCharactersState_NoOp | 22.9 | 23.8 | +4.0% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryCharactersState_Update | 7.4 | 7.6 | +2.4% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgCharactersState_NoOp | 86.2 | 88.0 | +2.1% | pass | 88 |
| ListVisualStateSetBenchmarks.OrgCharactersState_Update | 7.5 | 7.6 | +1.2% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgMapState_NoOp | 49.2 | 48.4 | -1.6% | pass | 72 |
| ListVisualStateSetBenchmarks.OrgMapState_Update | 7.6 | 7.6 | -0.2% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgActionsState_NoOp | 61.6 | 60.8 | -1.3% | pass | 96 |
| ListVisualStateSetBenchmarks.OrgActionsState_Update | 10.7 | 10.7 | -0.6% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryActionsState_NoOp | 48.4 | 33.8 | -30.2% | pass | 64 |
| ListVisualStateSetBenchmarks.CountryActionsState_Update | 11.6 | 7.7 | -33.6% | pass | 0 |
| ListVisualStateSetBenchmarks.LeaderboardState_NoOp | 283.5 | 189.7 | -33.1% | pass | 288 |
| ListVisualStateSetBenchmarks.LeaderboardState_Update | 11.2 | 7.8 | -30.7% | pass | 0 |
| ListVisualStateSetBenchmarks.GameLogState_NoOp | 23.3 | 15.1 | -35.4% | pass | 32 |
| ListVisualStateSetBenchmarks.GameLogState_Update | 7.5 | 5.2 | -31.5% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryResourcesState_NoOp | 13.2 | 22.9 | +74.3% | FAIL | 32 |
| ListVisualStateSetBenchmarks.CountryResourcesState_Update | 9.8 | 9.4 | -3.7% | pass | 0 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_NoOp | 12.2 | 12.2 | -0.1% | pass | 32 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_Update | 5.4 | 5.3 | -2.5% | pass | 0 |
| OrgScoreCollectorBenchmarks.Compute | 320.6 | 333.6 | +4.0% | pass | 472 |
| PopulationGrowthCollectorBenchmarks.Compute | 0.2 | 0.2 | +24.4% | FAIL | 0 |
| RecruitsGrowthCollectorBenchmarks.Compute | 5023.9 | 5134.8 | +2.2% | pass | 128 |
| RecruitsSeedCollectorBenchmarks.Compute | 5253.1 | 4990.6 | -5.0% | pass | 128 |
| ResourceQueryBenchmarks.GetValue | 5232.6 | 5185.4 | -0.9% | pass | 128 |
| ResourceSystemBenchmarks.Update_RegularDay | 177208.3 | 174500.9 | -1.5% | pass | 4464 |
| ResourceSystemBenchmarks.Update_MonthBoundary | 63466927.2 | 63949036.6 | +0.8% | pass | 13965670 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_NoOp | 0.2 | 0.2 | +24.1% | FAIL | 0 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_Update | 2.0 | 1.9 | -3.7% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_NoOp | 0.2 | 0.2 | -9.1% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_Update | 2.4 | 2.1 | -14.1% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_NoOp | 0.2 | 0.2 | +1.2% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_Update | 3.6 | 3.5 | -1.3% | pass | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_NoOp | 0.6 | 0.4 | -30.4% | pass | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_Update | 2.6 | 2.6 | -0.3% | pass | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_NoOp | 0.2 | 0.2 | -0.8% | pass | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_Update | 0.6 | 0.6 | -0.9% | pass | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_NoOp | 0.4 | 0.4 | +1.5% | pass | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_Update | 2.8 | 2.8 | 0.0% | pass | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_NoOp | 0.0 | 0.0 | +86.7% | FAIL | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_Update | 0.6 | 0.6 | -2.1% | pass | 0 |
| TimeSystemBenchmarks.Update | 13.5 | 10.3 | -24.1% | pass | 0 |
| VisualStateConverterBenchmarks.Update | 45201.3 | 45107.4 | -0.2% | pass | 18440 |
