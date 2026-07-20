# Benchmark Summary

> **Comparability caveat:** BenchmarkDotNet timings are machine- and environment-dependent. Baseline comparisons are only meaningful when `--compare` runs are produced on hardware comparable to the machine that produced the committed baseline (e.g. consistently within the same CI/dev-container class of machine) - no cross-machine normalization is attempted.

Mode: `compare`
Timestamp: 2026-07-20 18:29:24 UTC
Overall: PASS

| Benchmark | Baseline mean (ns) | Current mean (ns) | % change | Verdict | Allocated bytes |
|---|---|---|---|---|---|
| CountryPopulationCollectorBenchmarks.Compute | 269260.6 | 278342.2 | +3.4% | pass | 608859 |
