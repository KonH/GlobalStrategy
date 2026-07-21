# Game.Benchmarks

BenchmarkDotNet harness measuring the full per-tick `GameLogic.Update` cost and the individual
hot ECS systems/resource collectors it drives, against the real, fully-populated 163-country
world (`GameWorldFixture.Build()` reuses `Game.ConsoleRunner.Program.BuildContext` against the
committed `Assets/Configs` data - never a hand-built minimal fixture).

## Coverage

- `EcsWorldBenchmarks` - raw ECS entity creation, component addition, query mutation, direct
  component access (synthetic, entity-count-driven; not tied to game data volume by design).
- `VisualStateConverterBenchmarks` - steady-state `VisualStateConverter.Update` against the real
  fixture with a selected country.
- `FullTickBenchmarks` - `GameLogic.Update(24f)`, regular-day and month-boundary variants.
- `TimeSystemBenchmarks`, `ControlSystemBenchmarks` - unchanged per-tick systems.
- `ResourceSystemBenchmarks` - the ordered resource-collector pipeline, regular-day and
  month-boundary variants.
- `CountryPopulationCollectorBenchmarks`, `CountryScoreCollectorBenchmarks`,
  `PopulationGrowthCollectorBenchmarks`, `OrgScoreCollectorBenchmarks`,
  `RecruitsSeedCollectorBenchmarks`, `RecruitsGrowthCollectorBenchmarks` - individual
  `IResourceCollector.Compute` calls.
- `ResourceQueryBenchmarks` - the shared `ResourceQuery.GetValue` lookup.

## Run

BenchmarkDotNet requires a Release build for valid measurements - a Debug run prints an error
and exits before touching BenchmarkDotNet. Run from the repository root:

```powershell
dotnet run --project src/Game.Benchmarks -c Release -- --compare
dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark ControlSystem
dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline
```

- `--compare` runs the suite (or a `--benchmark <name>` substring-filtered subset) and gates
  each benchmark with an existing baseline entry against `Docs/Benchmarks/baseline.json`
  (`meanCurrent <= meanBaseline * (1 + epsilon)`, default `epsilon = 0.05`, override with
  `--epsilon <value>`). Exit code `0` iff every gated benchmark passes.
- `--update-baseline` runs the suite (optionally filtered) and overwrites the (filtered subset
  of) `Docs/Benchmarks/baseline.json` - an explicit, separate action from `--compare`.
- Both modes always append a record to `Docs/Benchmarks/history.json` and rewrite
  `Docs/Benchmarks/summary.md`.

See `Docs/Benchmarks/summary.md`'s header for the cross-machine-comparability caveat: baseline
comparisons are only meaningful when produced on hardware comparable to the committed baseline.
