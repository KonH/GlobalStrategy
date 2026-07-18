# GlobalStrategy Benchmarks

BenchmarkDotNet harness for measuring performance-sensitive plain .NET systems that can run outside Unity.

## Coverage

- `EcsWorldBenchmarks` measures raw ECS entity creation, component addition, query mutation, and direct component access.
- `GameSystemsBenchmarks` measures monthly `ResourceSystem` and `ControlSystem` updates over synthetic country/org/resource data.
- `GameLoopBenchmarks` measures repeated `GameLogic.Update` ticks against an in-memory playable configuration.

## Run

From the repository root:

```bash
dotnet run -c Release --project src/GlobalStrategy.Benchmarks/GlobalStrategy.Benchmarks.csproj -- --filter *
```

Use BenchmarkDotNet's standard command-line filters to run a subset, for example:

```bash
dotnet run -c Release --project src/GlobalStrategy.Benchmarks/GlobalStrategy.Benchmarks.csproj -- --filter *GameLoopBenchmarks*
```

Benchmark artifacts are emitted under `BenchmarkDotNet.Artifacts/` by default.
