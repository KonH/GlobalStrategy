# Benchmark Summary

> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container class of machine) - no cross-machine normalization is attempted.

Mode: `update-baseline`
Timestamp: 2026-07-23 14:29:34 UTC
Overall: FAIL

| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |
|---|---|---|---|---|---|
| ControlSystemBenchmarks.Update | 28810.8 | 75044.3 | +160.5% | FAIL | 1752 |
| CountryPopulationCollectorBenchmarks.Compute | 260735.8 | 934620.9 | +258.5% | FAIL | 608728 |
| CountryScoreCollectorBenchmarks.Compute | 4912.5 | 13311.6 | +171.0% | FAIL | 128 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_NoOp | - | 522655.7 | - | new - no baseline | 163943 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_Update | - | 22.3 | - | new - no baseline | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_NoOp | - | 94.9 | - | new - no baseline | 80 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_Update | - | 21.3 | - | new - no baseline | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_NoOp | - | 881.5 | - | new - no baseline | 832 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_Update | - | 8.4 | - | new - no baseline | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_NoOp | - | 311.2 | - | new - no baseline | 176 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_Update | - | 29.4 | - | new - no baseline | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=1000) | 7392.0 | 24324.1 | +229.1% | FAIL | 37880 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=1000) | 66076.1 | 196652.5 | +197.6% | FAIL | 136000 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=1000) | 1312.7 | 4732.7 | +260.5% | FAIL | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=1000) | 3239.7 | 11292.2 | +248.6% | FAIL | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=10000) | 124058.4 | 286885.5 | +131.3% | FAIL | 652684 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=10000) | 712886.3 | 2346221.9 | +229.1% | FAIL | 1818694 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=10000) | 12118.5 | 44257.8 | +265.2% | FAIL | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=10000) | 32945.2 | 115295.5 | +250.0% | FAIL | 0 |
| FullTickBenchmarks.Tick_RegularDay | 1522975.0 | 2035394.6 | +33.6% | FAIL | 132728 |
| FullTickBenchmarks.Tick_MonthBoundary | 60609761.5 | 155736106.9 | +156.9% | FAIL | 14088208 |
| ListVisualStateSetBenchmarks.CountryControlState_NoOp | - | 41.9 | - | new - no baseline | 32 |
| ListVisualStateSetBenchmarks.CountryControlState_Update | - | 11.1 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.CountryCharactersState_NoOp | - | 40.8 | - | new - no baseline | 32 |
| ListVisualStateSetBenchmarks.CountryCharactersState_Update | - | 8.1 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.OrgCharactersState_NoOp | - | 123.8 | - | new - no baseline | 88 |
| ListVisualStateSetBenchmarks.OrgCharactersState_Update | - | 8.0 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.OrgMapState_NoOp | - | 80.1 | - | new - no baseline | 72 |
| ListVisualStateSetBenchmarks.OrgMapState_Update | - | 8.5 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.OrgActionsState_NoOp | - | 108.2 | - | new - no baseline | 96 |
| ListVisualStateSetBenchmarks.OrgActionsState_Update | - | 11.0 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.CountryActionsState_NoOp | - | 83.0 | - | new - no baseline | 64 |
| ListVisualStateSetBenchmarks.CountryActionsState_Update | - | 11.3 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.LeaderboardState_NoOp | - | 389.0 | - | new - no baseline | 288 |
| ListVisualStateSetBenchmarks.LeaderboardState_Update | - | 11.7 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.GameLogState_NoOp | - | 38.3 | - | new - no baseline | 32 |
| ListVisualStateSetBenchmarks.GameLogState_Update | - | 8.4 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.CountryResourcesState_NoOp | - | 36.9 | - | new - no baseline | 32 |
| ListVisualStateSetBenchmarks.CountryResourcesState_Update | - | 19.4 | - | new - no baseline | 0 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_NoOp | - | 36.5 | - | new - no baseline | 32 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_Update | - | 9.4 | - | new - no baseline | 0 |
| OrgScoreCollectorBenchmarks.Compute | 306.4 | 1072.7 | +250.1% | FAIL | 472 |
| PopulationGrowthCollectorBenchmarks.Compute | 0.0 | 0.1 | +528.1% | FAIL | 0 |
| RecruitsGrowthCollectorBenchmarks.Compute | 4958.8 | 13354.5 | +169.3% | FAIL | 128 |
| RecruitsSeedCollectorBenchmarks.Compute | 5110.5 | 13448.7 | +163.2% | FAIL | 128 |
| ResourceQueryBenchmarks.GetValue | 5103.8 | 13367.8 | +161.9% | FAIL | 128 |
| ResourceSystemBenchmarks.Update_RegularDay | 149507.5 | 472370.4 | +216.0% | FAIL | 4464 |
| ResourceSystemBenchmarks.Update_MonthBoundary | 61662085.4 | 159196842.1 | +158.2% | FAIL | 13959072 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_NoOp | - | 0.6 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_Update | - | 3.9 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_NoOp | - | 3.6 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_Update | - | 6.0 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_NoOp | - | 1.2 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_Update | - | 10.6 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_NoOp | - | 3.9 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_Update | - | 5.8 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_NoOp | - | 1.4 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_Update | - | 1.8 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_NoOp | - | 1.3 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_Update | - | 7.5 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_NoOp | - | 0.7 | - | new - no baseline | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_Update | - | 1.9 | - | new - no baseline | 0 |
| TimeSystemBenchmarks.Update | 9.8 | 38.7 | +294.6% | FAIL | 0 |
| VisualStateConverterBenchmarks.Update | 43758.3 | 138732.1 | +217.0% | FAIL | 18440 |
