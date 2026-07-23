# Benchmark Summary

> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container class of machine) - no cross-machine normalization is attempted.

Mode: `compare`
Timestamp: 2026-07-23 20:59:34 UTC
Overall: FAIL

| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |
|---|---|---|---|---|---|
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_NoOp | 228372.0 | 218794.6 | -4.2% | pass | 163709 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOwnershipState_Update | 10.4 | 10.4 | +0.4% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_NoOp | 24.5 | 24.6 | +0.3% | pass | 80 |
| DictionaryAndSetVisualStateSetBenchmarks.ProvinceOccupationState_Update | 10.9 | 10.9 | +0.3% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_NoOp | 471.5 | 463.6 | -1.7% | pass | 832 |
| DictionaryAndSetVisualStateSetBenchmarks.CountryScoreState_Update | 2.9 | 2.8 | -5.2% | pass | 0 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_NoOp | 84.8 | 83.6 | -1.5% | pass | 176 |
| DictionaryAndSetVisualStateSetBenchmarks.DiscoveredCountriesState_Update | 9.6 | 11.6 | +21.2% | FAIL | 0 |
| ListVisualStateSetBenchmarks.CountryControlState_NoOp | 14.6 | 14.3 | -2.0% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryControlState_Update | 8.0 | 5.0 | -37.0% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryCharactersState_NoOp | 22.9 | 14.6 | -36.2% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryCharactersState_Update | 7.4 | 4.6 | -37.3% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgCharactersState_NoOp | 86.2 | 48.2 | -44.1% | pass | 88 |
| ListVisualStateSetBenchmarks.OrgCharactersState_Update | 7.5 | 5.2 | -30.8% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgMapState_NoOp | 49.2 | 32.8 | -33.4% | pass | 72 |
| ListVisualStateSetBenchmarks.OrgMapState_Update | 7.6 | 5.1 | -32.6% | pass | 0 |
| ListVisualStateSetBenchmarks.OrgActionsState_NoOp | 61.6 | 39.7 | -35.6% | pass | 96 |
| ListVisualStateSetBenchmarks.OrgActionsState_Update | 10.7 | 7.2 | -33.0% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryActionsState_NoOp | 48.4 | 31.3 | -35.3% | pass | 64 |
| ListVisualStateSetBenchmarks.CountryActionsState_Update | 11.6 | 7.7 | -33.3% | pass | 0 |
| ListVisualStateSetBenchmarks.LeaderboardState_NoOp | 283.5 | 185.9 | -34.4% | pass | 288 |
| ListVisualStateSetBenchmarks.LeaderboardState_Update | 11.2 | 7.3 | -35.2% | pass | 0 |
| ListVisualStateSetBenchmarks.GameLogState_NoOp | 23.3 | 14.6 | -37.5% | pass | 32 |
| ListVisualStateSetBenchmarks.GameLogState_Update | 7.5 | 5.3 | -29.0% | pass | 0 |
| ListVisualStateSetBenchmarks.CountryResourcesState_NoOp | 13.2 | 12.6 | -4.5% | pass | 32 |
| ListVisualStateSetBenchmarks.CountryResourcesState_Update | 9.8 | 9.5 | -2.4% | pass | 0 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_NoOp | 12.2 | 12.2 | -0.3% | pass | 32 |
| ListVisualStateSetBenchmarks.VisualEffectCollection_Update | 5.4 | 5.2 | -3.5% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_NoOp | 0.2 | 0.2 | +10.6% | FAIL | 0 |
| ScalarVisualStateSetBenchmarks.SelectedCountryState_Update | 2.0 | 2.0 | -0.2% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_NoOp | 0.2 | 0.2 | -6.6% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedOrganizationState_Update | 2.4 | 2.5 | +1.8% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_NoOp | 0.2 | 0.2 | +0.6% | pass | 0 |
| ScalarVisualStateSetBenchmarks.SelectedProvinceState_Update | 3.6 | 3.5 | -2.0% | pass | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_NoOp | 0.6 | 0.6 | -2.9% | pass | 0 |
| ScalarVisualStateSetBenchmarks.PlayerOrganizationState_Update | 2.6 | 2.2 | -15.0% | pass | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_NoOp | 0.2 | 0.2 | +0.8% | pass | 0 |
| ScalarVisualStateSetBenchmarks.TimeState_Update | 0.6 | 0.6 | -1.4% | pass | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_NoOp | 0.4 | 0.4 | +1.2% | pass | 0 |
| ScalarVisualStateSetBenchmarks.LocaleState_Update | 2.8 | 2.7 | -2.4% | pass | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_NoOp | 0.0 | 0.0 | -100.0% | pass | 0 |
| ScalarVisualStateSetBenchmarks.MapLensState_Update | 0.6 | 0.6 | -3.2% | pass | 0 |
| VisualStateConverterBenchmarks.Update | 45201.3 | 45466.3 | +0.6% | pass | 18440 |
