# Benchmark Summary

> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container class of machine) - no cross-machine normalization is attempted.

Mode: `update-baseline`
Timestamp: 2026-07-23 19:45:34 UTC
Overall: FAIL

| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |
|---|---|---|---|---|---|
| ControlSystemBenchmarks.Update | 75044.3 | 61122.8 | -18.6% | pass | 1752 |
| CountryPopulationCollectorBenchmarks.Compute | 934620.9 | 422477.0 | -54.8% | pass | 608862 |
| CountryScoreCollectorBenchmarks.Compute | 13311.6 | 5204.0 | -60.9% | pass | 128 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_NoOp | 522655.7 | 228372.0 | -56.3% | pass | 163709 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_Update | 22.3 | 10.4 | -53.5% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_NoOp | 94.9 | 24.5 | -74.2% | pass | 80 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_Update | 21.3 | 10.9 | -49.0% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_NoOp | 881.5 | 471.5 | -46.5% | pass | 832 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_Update | 8.4 | 2.9 | -65.6% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_NoOp | 311.2 | 84.8 | -72.7% | pass | 176 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_Update | 29.4 | 9.6 | -67.3% | pass | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=1000) | 24324.1 | 8483.4 | -65.1% | pass | 37880 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=1000) | 196652.5 | 64299.4 | -67.3% | pass | 136000 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=1000) | 4732.7 | 1304.8 | -72.4% | pass | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=1000) | 11292.2 | 3259.9 | -71.1% | pass | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=10000) | 286885.5 | 126469.1 | -55.9% | pass | 652642 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=10000) | 2346221.9 | 719299.7 | -69.3% | pass | 1818627 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=10000) | 44257.8 | 12467.7 | -71.8% | pass | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=10000) | 115295.5 | 33419.2 | -71.0% | pass | 0 |
| FullTickBenchmarks.Tick_RegularDay | 2035394.6 | 1570900.0 | -22.8% | pass | 132056 |
| FullTickBenchmarks.Tick_MonthBoundary | 155736106.9 | 65362562.2 | -58.0% | pass | 14088208 |
| ListVisualStateSetBenchmarks.CountryControlState_NoOp | 41.9 | 14.6 | -65.2% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryControlState_Update | 11.1 | 8.0 | -28.0% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryCharactersState_NoOp | 40.8 | 22.9 | -43.9% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryCharactersState_Update | 8.1 | 7.4 | -8.4% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgCharactersState_NoOp | 123.8 | 86.2 | -30.4% | pass | 88 |
| ListVisualStateSetBenchmarks.OrgCharactersState_Update | 8.0 | 7.5 | -6.7% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgMapState_NoOp | 80.1 | 49.2 | -38.6% | pass | 72 |
| ListVisualStateSetBenchmarks.OrgMapState_Update | 8.5 | 7.6 | -10.9% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgActionsState_NoOp | 108.2 | 61.6 | -43.1% | pass | 96 |
| ListVisualStateSetBenchmarks.OrgActionsState_Update | 11.0 | 10.7 | -2.4% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryActionsState_NoOp | 83.0 | 48.4 | -41.7% | pass | 64 |
| ListVisualStateSetBenchmarks.CountryActionsState_Update | 11.3 | 11.6 | +2.5% | pass | 0 |
| ListVisualStateSetBenchmarks.LeaderboardState_NoOp | 389.0 | 283.5 | -27.1% | pass | 288 |
| ListVisualStateSetBenchmarks.LeaderboardState_Update | 11.7 | 11.2 | -4.5% | pass | 0 |
| ListVisualStateSetBenchmarks.GameLogState_NoOp | 38.3 | 23.3 | -39.2% | pass | 32 |
| ListVisualStateSetBenchmarks.GameLogState_Update | 8.4 | 7.5 | -10.5% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryResourcesState_NoOp | 36.9 | 13.2 | -64.4% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryResourcesState_Update | 19.4 | 9.8 | -49.6% | pass | 0 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_NoOp | 36.5 | 12.2 | -66.6% | pass | 32 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_Update | 9.4 | 5.4 | -42.9% | pass | 0 |
| OrgScoreCollectorBenchmarks.Compute | 1072.7 | 320.6 | -70.1% | pass | 472 |
| PopulationGrowthCollectorBenchmarks.Compute | 0.1 | 0.2 | +78.7% | FAIL | 0 |
| RecruitsGrowthCollectorBenchmarks.Compute | 13354.5 | 5023.9 | -62.4% | pass | 128 |
| RecruitsSeedCollectorBenchmarks.Compute | 13448.7 | 5253.1 | -60.9% | pass | 128 |
| ResourceQueryBenchmarks.GetValue | 13367.8 | 5232.6 | -60.9% | pass | 128 |
| ResourceSystemBenchmarks.Update_RegularDay | 472370.4 | 177208.3 | -62.5% | pass | 4464 |
| ResourceSystemBenchmarks.Update_MonthBoundary | 159196842.1 | 63466927.2 | -60.1% | pass | 13965628 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_NoOp | 0.6 | 0.2 | -71.0% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_Update | 3.9 | 2.0 | -48.1% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_NoOp | 3.6 | 0.2 | -93.3% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_Update | 6.0 | 2.4 | -59.3% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_NoOp | 1.2 | 0.2 | -83.1% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_Update | 10.6 | 3.6 | -66.1% | pass | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_NoOp | 3.9 | 0.6 | -84.4% | pass | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_Update | 5.8 | 2.6 | -55.0% | pass | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_NoOp | 1.4 | 0.2 | -85.7% | pass | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_Update | 1.8 | 0.6 | -68.8% | pass | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_NoOp | 1.3 | 0.4 | -69.5% | pass | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_Update | 7.5 | 2.8 | -62.7% | pass | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_NoOp | 0.7 | 0.0 | -99.8% | pass | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_Update | 1.9 | 0.6 | -68.2% | pass | 0 |
| TimeSystemBenchmarks.Update | 38.7 | 13.5 | -65.1% | pass | 0 |
| VisualStateConverterBenchmarks.Update | 138732.1 | 45201.3 | -67.4% | pass | 18440 |
