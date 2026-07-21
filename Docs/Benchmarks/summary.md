# Benchmark Summary

> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container class of machine) - no cross-machine normalization is attempted.

Mode: `update-baseline`
Timestamp: 2026-07-20 18:40:26 UTC
Overall: FAIL

| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |
|---|---|---|---|---|---|
| ControlSystemBenchmarks.Update | 28894.7 | 28810.8 | -0.3% | pass | 1752 |
| CountryPopulationCollectorBenchmarks.Compute | 269260.6 | 260735.8 | -3.2% | pass | 608992 |
| CountryScoreCollectorBenchmarks.Compute | 5126.9 | 4912.5 | -4.2% | pass | 128 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=1000) | 7891.7 | 7392.0 | -6.3% | pass | 37880 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=1000) | 66568.8 | 66076.1 | -0.7% | pass | 136000 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=1000) | 1392.3 | 1312.7 | -5.7% | pass | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=1000) | 3296.0 | 3239.7 | -1.7% | pass | 0 |
| EcsWorldBenchmarks.CreateEntities(EntityCount=10000) | 126058.9 | 124058.4 | -1.6% | pass | 652642 |
| EcsWorldBenchmarks.AddTwoComponents(EntityCount=10000) | 732200.8 | 712886.3 | -2.6% | pass | 1818627 |
| EcsWorldBenchmarks.QueryTwoComponentsAndMutate(EntityCount=10000) | 12162.9 | 12118.5 | -0.4% | pass | 352 |
| EcsWorldBenchmarks.GetComponentByEntity(EntityCount=10000) | 33775.9 | 32945.2 | -2.5% | pass | 0 |
| FullTickBenchmarks.Tick_RegularDay | 1604860.0 | 1522975.0 | -5.1% | pass | 123208 |
| FullTickBenchmarks.Tick_MonthBoundary | 61695455.6 | 60609761.5 | -1.8% | pass | 14079360 |
| OrgScoreCollectorBenchmarks.Compute | 312.2 | 306.4 | -1.9% | pass | 472 |
| PopulationGrowthCollectorBenchmarks.Compute | 0.0 | 0.0 | +39.4% | FAIL | 0 |
| RecruitsGrowthCollectorBenchmarks.Compute | 5146.1 | 4958.8 | -3.6% | pass | 128 |
| RecruitsSeedCollectorBenchmarks.Compute | 4920.0 | 5110.5 | +3.9% | pass | 128 |
| ResourceQueryBenchmarks.GetValue | 4980.7 | 5103.8 | +2.5% | pass | 128 |
| ResourceSystemBenchmarks.Update_RegularDay | 160127.9 | 149507.5 | -6.6% | pass | 4464 |
| ResourceSystemBenchmarks.Update_MonthBoundary | 64957427.7 | 61662085.4 | -5.1% | pass | 13965303 |
| TimeSystemBenchmarks.Update | 9.6 | 9.8 | +1.9% | pass | 0 |
| VisualStateConverterBenchmarks.Update | 43155.4 | 43758.3 | +1.4% | pass | 18224 |
