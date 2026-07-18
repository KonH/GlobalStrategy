# Plan: BenchmarkDotNet Performance Harness & Optimization Skills

## Spec

Source: `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/spec.md`.

**Intent.** A BenchmarkDotNet-based harness measuring the full per-tick `GameLogic.Update` cost and named hot ECS systems, committed baseline snapshots under `Docs/Benchmarks/` so regressions/improvements are visible in git history, plus two skills: `/benchmark-report` (human-driven compare/report) and `/optimize-performance` (bounded Ralph-loop autonomous optimization with a Constitution carve-out), mirroring `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`'s shape for wall-clock/allocation performance instead of gameplay score.

**Resolved decisions (from `/specify`, binding):**
- Benchmark both the full tick and individual hot systems (not one or the other).
- Snapshots committed under `Docs/Benchmarks/` (baseline + append-only history + rewritten summary) — never gitignored.
- Both skills are in scope: `/benchmark-report` (no autonomy) and `/optimize-performance` (bounded Ralph loop, ends in PR).

**Correction #1 (found during planning): `OrgScoreSystem` is ticked every frame.** The spec's "What is benchmarked" section states `OrgScoreSystem`'s scoring "is not ticked every frame inside `GameLogic.Update`." That is incorrect — `GameLogic.Update` calls `OrgScoreSystem.Update(_world, _previousTime, currentTime)` unconditionally every tick (`src/Game.Main/GameLogic.cs:164`). Separately, `OrgScoreSystem.GetScore(IReadOnlyWorld world, string orgId)` is a distinct derived query the eval harness calls once per org per sample (`src/Game.ConsoleRunner/HeadlessRunner.cs:197`). This plan benchmarks **both**.

**Correction #2 (found during planning, larger): the spec's named systems are being deleted out from under this plan.** The spec names `ProvincePopulationGrowthSystem` and `CountryScoreSystem` (its `Update`/`Recompute` methods) as individually-benchmarked hot systems. Two already-approved, not-yet-implemented plans sitting immediately ahead of this one in the spec sequence remove exactly those:

- `Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md` deletes `src/Game.Systems/ProvincePopulationGrowthSystem.cs` entirely (migrating province population growth to a `Monthly` collector effect processed generically by `ResourceSystem`) and trims `CountryScoreSystem` down to `GetScore` only, deleting `Update`/`Recompute` (country score becomes a `Resource{ResourceId="country_score"}` fed by a `CountryScoreCollector`, resolved through a new `IResourceCollector`/`ResourceCollectorRegistry` abstraction and a `resourceIdUpdateOrder`-driven resolve-then-apply pass added to `ResourceSystem.Update`). It also changes `OrgScoreSystem.Recompute`'s read path to call `CountryScoreSystem.GetScore` per country instead of iterating `Country + Score` archetypes.
- `Docs/Specs/26_07_18_15_recruits-resource/plan.md` (dependent on the above) adds a fifth resourceId, `recruits`, via two more collectors (`RecruitsSeedCollector`, `RecruitsGrowthCollector`) and a shared `ResourceQuery.GetValue(world, ownerId, resourceId)` lookup helper.

Neither is implemented in the live repo as of this plan (confirmed: `ProvincePopulationGrowthSystem.cs` still exists, `CountryScoreSystem.Update`/`.Recompute` still exist, `ResourceSystem.Update` still takes only `(World, DateTime, DateTime)` — verified directly against current `src/` at planning time). Building this harness against the systems the spec named would make it stale the moment the pipeline plan lands — the exact scenario this feature exists to prevent (a regression nobody notices because the thing that would have caught it was deleted first). **This plan therefore benchmarks the actual replacement architecture** (§4 below) instead of the soon-to-be-deleted systems the spec happened to name, and takes a hard dependency on the pipeline plan landing first (see "Hard dependency" below). This does not change the spec's intent — "benchmark the deterministic per-tick hot systems plus the org/country scoring queries" — only which concrete types satisfy it.

**Key acceptance criteria (design targets, updated for Correction #2):**
- New net8.0 exe `src/Game.Benchmarks`, added to `src/GlobalStrategy.Core.sln`, referencing BenchmarkDotNet; never emitted to `Assets/Plugins/Core/`; no `Assets/` change of any kind. Must be run as `dotnet run --project src/Game.Benchmarks -c Release -- <args>` — Debug runs are rejected with a clear error.
- Fixture built once per benchmark class via a shared helper reusing the existing config-loading/bootstrap path (`Program.BuildContext` + `GameLogicContext` + `GameLogic`), producing the real, fully-populated 163-country/province/org world — not a hand-built minimal fixture.
- Benchmarked: full `GameLogic.Update(deltaTime)` tick, in both a regular-day and a month-boundary variant (the collector pipeline's expensive path only fires on month boundaries — see §4); `TimeSystem`, `ControlSystem` individually (unchanged systems); the **ordered `ResourceSystem.Update` pipeline** individually, in both variants — this is the harness's primary "resource update flow" target; the collectors driving that pipeline (`CountryPopulationCollector`, `CountryScoreCollector`, `PopulationGrowthCollector`, plus the recruits collectors if that plan has also landed) and the shared `ResourceQuery.GetValue` lookup, each individually; `CountryScoreSystem.GetScore`, `OrgScoreSystem.Update`, `OrgScoreSystem.GetScore` individually. `[MemoryDiagnoser]` on every class.
- Snapshots: `Docs/Benchmarks/baseline.json` (current accepted numbers), `Docs/Benchmarks/history.json` (append-only, one entry per `--compare`/`--update-baseline` run, never rewritten), `Docs/Benchmarks/summary.md` (rewritten every run, documents the cross-machine-comparability caveat in its header).
- `--compare` mode: runs the suite (or a `--benchmark <name>` filtered subset), gates each benchmark with an existing baseline entry on `meanCurrent ≤ meanBaseline × (1 + epsilonRelative)` (default `0.05`), exit code `0` iff every gated benchmark passes; a benchmark with no baseline entry auto-passes ("new — no baseline"). Allocated bytes reported, never gated in v1.
- `--update-baseline` mode: runs the suite (optionally filtered), always appends to history + rewrites summary, then overwrites the (filtered subset of) `baseline.json` — an explicit, separate action from `--compare`.
- `/benchmark-report` skill: runs `--compare` (or `--update-baseline` when `$ARGUMENTS == "update-baseline"`), shows the table in chat; no source edits, no commit, no PR.
- `/optimize-performance <target>` skill: mirrors `/implement-bot-feature`'s shape — authority statement scoping it to micro-optimizations of already-implemented `src/` game-core logic; branch `ralph/perf_<target>`; writes `.ralph/prd.md` directly with a gate of `dotnet test src/GlobalStrategy.Core.sln && dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark <target>`; bounded attempt budget (default 5) reading `Docs/Benchmarks/history.json` + `.ralph/activity.md` between attempts; on success runs `--update-baseline`, commits, hands off to `/complete-prd perf:<target>`; on budget exhaustion, no baseline update, no PR, everything left for a human.
- Constitution amendment: a second Planning Discipline carve-out (alongside the existing bot-feature one) for `/optimize-performance`-driven changes within its authority statement, standing spec/plan = this pair.

**Out of scope (unchanged from spec):** CI integration, cross-machine baseline normalization, Unity/Assets profiling, allocation-based gating, automatic runtime revert-on-regression, a genetic/statistical optimizer, benchmarking startup/save-load (deferred to a follow-up spec).

## Hard dependency — this plan cannot start before it lands

`Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md` must be implemented and merged first. This plan's fixture, benchmark list, and dependency-preflight step (first Agent Step below) all assume its exact shapes exist: `IResourceCollector`, `ResourceCollectorRegistry` (`public static CreateDefault(...)` + `public Resolve(string collectorId)`), the `ResourceSystem.Update(World, DateTime, DateTime, ResourceCollectorRegistry?, IReadOnlyList<string>?)` overload, `CountryScoreSystem` trimmed to `GetScore` only, and `OrgScoreSystem.Recompute` reading via `CountryScoreSystem.GetScore`. If any of these differ from what `26_07_18_17_resource-collector-pipeline/plan.md` describes by the time this plan is implemented, stop and reconcile before writing code — do not code around a drifted dependency.

`Docs/Specs/26_07_18_15_recruits-resource/plan.md` (which itself depends on the above) is a **soft** dependency: this plan's fixture and `ResourceSystem`/full-tick benchmarks work correctly whether or not it has landed (they read `GameSettings.ResourceIdUpdateOrder` verbatim from the committed config, whatever it contains), but the two recruits-specific collector benchmarks (`RecruitsSeedCollectorBenchmarks`, `RecruitsGrowthCollectorBenchmarks`) are only added if `RecruitsSeedCollector`/`RecruitsGrowthCollector` actually exist at implementation time. The preflight step checks for their presence and includes or skips those two files accordingly — this is a normal conditional in the implementation step, not a blocker.

## Goal

Add `src/Game.Benchmarks` (net8.0 BenchmarkDotNet host) with one fixture builder and a benchmark suite covering the full tick, the two unchanged per-tick systems, the ordered resource-collector pipeline (both cheap and expensive variants), every individual collector plus the shared resource-lookup helper, and the country/org scoring queries — all wired to a `--compare`/`--update-baseline` CLI built on BenchmarkDotNet's in-process `Summary` results (no dependency on BenchmarkDotNet's own JSON exporter file layout); widen `Game.ConsoleRunner.Program.BuildContext` from `internal` to `public` (the one production-code visibility change this plan needs — no other production visibility changes are required, since every collector is reachable through the already-public `ResourceCollectorRegistry`/`IResourceCollector`/`ResourceQuery` surface the pipeline plan defines); seed `Docs/Benchmarks/` with a real initial baseline/history/summary produced by an actual `--update-baseline` run; add `.claude/commands/benchmark-report.md` and `.claude/commands/optimize-performance.md`; extend `scripts/ralph.py`/`ralph.ps1` with a `--perf-target`/`-PerfTarget` mode paralleling the existing `--bot-feature`/`-BotFeature` mode; extend `.claude/commands/complete-prd.md` for a `perf:<target>` argument form; amend `Docs/Constitution.md`'s Planning Discipline section with the performance carve-out; add isolated ECS-world correctness tests (§ Tests) proving each benchmark's chosen inputs actually exercise real work, since BenchmarkDotNet itself only measures time and would happily report a fast number for a silently-broken no-op. All new code is `src/`-side or `.claude`/`scripts`/`Docs`-side; no Unity asset, scene, or script changes; the only Unity-visible side effect is the usual `Assets/Plugins/Core/` DLL refresh from rebuilding `src/` in Release.

## Approach

### 1. `src/Game.Benchmarks` project

New net8.0 exe, added to `src/GlobalStrategy.Core.sln` (new project GUID, mirroring the existing `Game.Evals` entry format) and to its `Debug|Release × Any CPU` configuration blocks:

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<OutputType>Exe</OutputType>
		<TargetFramework>net8.0</TargetFramework>
		<Nullable>enable</Nullable>
		<LangVersion>latestMajor</LangVersion>
	</PropertyGroup>
	<ItemGroup>
		<PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
		<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
	</ItemGroup>
	<ItemGroup>
		<ProjectReference Include="../ECS.Core/ECS.Core.csproj" />
		<ProjectReference Include="../Game.Commands/Game.Commands.csproj" />
		<ProjectReference Include="../Game.Components/Game.Components.csproj" />
		<ProjectReference Include="../Game.Systems/Game.Systems.csproj" />
		<ProjectReference Include="../Game.Main/Game.Main.csproj" />
		<ProjectReference Include="../Game.ConsoleRunner/Game.ConsoleRunner.csproj" />
	</ItemGroup>
</Project>
```

`Newtonsoft.Json` is used for the harness's own `baseline.json`/`history.json` persistence (project convention — never `System.Text.Json` in anything that could plausibly move toward Unity, and consistent with the sibling exe projects). Never emitted to `Assets/Plugins/Core/` — same posture as `Game.Evals`/`Game.ConsoleRunner`.

**Release-only guard.** `Program.Main` reads `#if DEBUG` (compile-time, reliable) and, if defined, prints `"Game.Benchmarks must be run with -c Release (BenchmarkDotNet requires a Release build for valid measurements)."` to stderr and exits `2` before touching BenchmarkDotNet at all.

### 2. Shared fixture builder (`GameWorldFixture.cs`)

```csharp
namespace GS.Game.Benchmarks {
	static class GameWorldFixture {
		public const string ConfigDir = "Assets/Configs";
		public const int Seed = 1880;

		public readonly struct Fixture {
			public readonly GameLogic Logic;
			public readonly int GameTimeEntity;
			public readonly ResourceCollectorRegistry CollectorRegistry;
			public readonly IReadOnlyList<string> ResourceIdUpdateOrder;
			public readonly string FirstOrgId;
			public readonly string FirstCountryId;
			// constructor omitted for brevity
		}

		public static Fixture Build() {
			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(ConfigDir, "organizations.json")).Load();
			var orgIds = new List<string>();
			foreach (var entry in orgConfig.Organizations) { orgIds.Add(entry.OrganizationId); }
			var ctx = Program.BuildContext(ConfigDir, rngSeed: Seed, participatingOrganizationIds: orgIds,
				initialOrganizationId: orgIds.Count > 0 ? orgIds[0] : "", logger: null);
			var logic = new GameLogic(ctx);
			logic.Update(24f); // triggers InitSystem once — populates all countries/provinces/orgs from the committed configs

			int gameTimeEntity = -1;
			int[] required = { TypeId<GameTime>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(required, null)) {
				gameTimeEntity = arch.Entities[0];
				break;
			}
			if (gameTimeEntity < 0) {
				throw new InvalidOperationException("GameWorldFixture: no GameTime entity found after init tick.");
			}

			// Independently loads the same committed game_settings.json GameLogic's own
			// constructor already read, so the standalone registry below matches production
			// values exactly without needing any new GameLogic API.
			var settings = new FileConfig<GameSettings>(Path.Combine(ConfigDir, "game_settings.json")).Load();
			var registry = ResourceCollectorRegistry.CreateDefault(/* settings fields per whichever
				CreateDefault overload is live — see Approach §4's preflight note */);

			return new Fixture(logic, gameTimeEntity, registry, settings.ResourceIdUpdateOrder,
				orgIds.Count > 0 ? orgIds[0] : "", /* first available countryId from CountryConfig */ "");
		}
	}
}
```

- Requires `Game.ConsoleRunner.Program.BuildContext` to be `public` (see §3). Reuses it directly rather than duplicating config-loading — the one production change this plan needs.
- `24f` matches `HeadlessRunner`'s own `deltaTime = hoursPerTick / speedMultipliers[0]` convention — one `Update` call advances exactly one day and (this being the very first call) also runs `InitSystem`, so the returned world is the real, fully-populated 163-country/province/org world at day 2 of the configured start year. No hand-built minimal world.
- The `GetMatchingArchetypes` scan for the singleton `GameTime` entity is the documented pattern from `.claude/rules/unity/ecs_patterns.md` ("Singleton entities").
- Building a **second, standalone** `ResourceCollectorRegistry` (rather than reaching into `GameLogic`'s own private one) is deliberate: every collector benchmark below calls `registry.Resolve(collectorId).Compute(...)` directly, and `ResourceCollectorRegistry.CreateDefault`/`Resolve` are both public per the pipeline plan — no additional visibility widening needed anywhere in `Game.Systems`.
- Each `[Benchmark]` class calls `GameWorldFixture.Build()` independently in its own `[GlobalSetup]` (BenchmarkDotNet does not share state across classes) — cost is amortized (excluded from measurement) and each class benchmarks against its own untouched-by-other-classes world instance.
- **No per-iteration reset, with two explicit exceptions.** Systems/collectors benchmarked here iterate a fixed number of ECS entities (countries/provinces/orgs) per call; their cost is dominated by entity counts, not by the numeric magnitude of the values they mutate. Letting values compound across a benchmark run's many iterations does not skew the measured per-call cost and avoids paying full fixture-rebuild cost thousands of times per run. The two exceptions, both in `FullTickBenchmarks` (§4): `Tick_RegularDay`/`Tick_MonthBoundary` drive `previousTime`/`currentTime` off the mutable `GameTime` component inside `logic.World`, not off explicit parameters, so without a reset every iteration would just be "whatever day comes next" — only ~1 in 30 iterations would land on a month boundary, diluting the exact case this harness most needs to see clearly. Both benchmarks use `[IterationSetup]` to force their date onto a fixed pre-iteration value (see §4) — excluded from measurement, so it does not affect timing, only which branch each iteration takes.

### 3. `Program.BuildContext` visibility (`src/Game.ConsoleRunner/Program.cs`)

One-line change: `internal static GameLogicContext BuildContext(...)` → `public static GameLogicContext BuildContext(...)`. Purely additive visibility widening (no behavior change, no signature change); `Game.Evals` and `RunInteractive` keep calling it exactly as before. This is the only production-code change this plan needs — every other type the benchmarks touch (`ResourceCollectorRegistry`, `IResourceCollector`, `ResourceQuery`, `CountryScoreSystem.GetScore`, `OrgScoreSystem.Update`/`GetScore`) is already public per the pipeline/recruits plans.

### 4. Benchmark classes (one file per benchmarked thing, mirroring `Game.Systems`' one-system-per-file convention)

| File | `[Benchmark]` method(s) | What it calls | Notes |
|---|---|---|---|
| `FullTickBenchmarks.cs` | `Tick_RegularDay()`, `Tick_MonthBoundary()` | `logic.Update(24f)` | `[IterationSetup]` sets `GameTime.CurrentTime` to a fixed mid-month date (regular) or the last day of a month (boundary) before every iteration — see §2. This is the harness's headline number: the boundary variant is the expensive tick that runs the full ordered collector pipeline for 163 countries + ~1,200+ provinces; the regular variant is every other day. |
| `TimeSystemBenchmarks.cs` | `Update()` | `TimeSystem.Update(world, gameTimeEntity, 24f, speedMultipliers, default, default, default)` | Unchanged system. `default(ReadCommands<T>)` gives an empty-queue read with no `CommandAccessor` needed (that type is `internal` to `Game.Main`; `ReadCommands<T>`'s own default is a valid empty read). |
| `ControlSystemBenchmarks.cs` | `Update()` | `ControlSystem.Update(world, previousTime, previousTime.AddDays(1))` | Unchanged system, not touched by the pipeline plans. |
| `ResourceSystemBenchmarks.cs` | `Update_RegularDay()`, `Update_MonthBoundary()` | `ResourceSystem.Update(world, previousTime, currentTime, registry, resourceIdUpdateOrder)` — `currentTime = previousTime.AddDays(1)` (regular) or a fixed month-end→next-day pair (boundary) | **The primary "resource update flow" target** the pipeline plans introduce: the ordered resolve-then-apply pass over every configured resourceId (`population`, `country_population`, `country_score`, and `recruits` if that plan has landed) plus the unordered fallback pass (gold, character skills, opinion). No `[IterationSetup]` needed here — unlike the full tick, this system takes `previousTime`/`currentTime` as explicit parameters rather than reading them from `GameTime`, so a fixed month-boundary pair reused every iteration reliably re-triggers the expensive path every single call. |
| `CountryPopulationCollectorBenchmarks.cs` | `Compute()` | `registry.Resolve("country_population_aggregate").Compute(countryId, currentValue, world)` | The pipeline plan's own text flags this collector as a deliberately-accepted O(countries × provinces) tradeoff ("not optimized in this plan... if it grows enough to matter") — exactly the kind of thing this harness exists to watch, and the most likely first target for `/optimize-performance CountryPopulationCollector`. |
| `CountryScoreCollectorBenchmarks.cs` | `Compute()` | `registry.Resolve("country_score_formula").Compute(countryId, currentValue, world)` | Cheap single-lookup collector (reads `country_population` via `ResourceQuery.GetValue`). |
| `PopulationGrowthCollectorBenchmarks.cs` | `Compute()` | `registry.Resolve("population_growth").Compute(provinceId, currentValue, world)` | Pure O(1) function — a low/flat baseline for comparison against the two above. |
| `ResourceQueryBenchmarks.cs` | `GetValue()` | `ResourceQuery.GetValue(world, countryId, "country_population")` | Shared helper (`26_07_18_15_recruits-resource`'s extraction) called once per collector resolve per country — a full archetype scan per call, so its own cost multiplies across every caller; worth watching independently of any one collector. |
| `CountryScoreSystemBenchmarks.cs` | `GetScore()` | `CountryScoreSystem.GetScore(world, countryId)` | The only member left on `CountryScoreSystem` post-migration; read once per country by `OrgScoreSystem.Recompute` (see next row) and potentially by future UI. |
| `OrgScoreSystemBenchmarks.cs` | `UpdateTick()`, `GetScore()` | `OrgScoreSystem.Update(world, previousTime, previousTime.AddDays(1))`; `OrgScoreSystem.GetScore(world, orgId)` | `Update`'s `Recompute` now calls `CountryScoreSystem.GetScore` once per country internally (per the pipeline plan's §7) — a second latent O(countries × scan) shape worth watching alongside `CountryPopulationCollector`'s. |
| `RecruitsSeedCollectorBenchmarks.cs`, `RecruitsGrowthCollectorBenchmarks.cs` | `Compute()` | `registry.Resolve("recruits_seed"/"recruits_growth").Compute(countryId, currentValue, world)` | **Conditional** — only added if `RecruitsSeedCollector`/`RecruitsGrowthCollector` exist at implementation time (soft dependency, see "Hard dependency" section above). |

Each class:
- Is `[MemoryDiagnoser]`.
- Has a `[GlobalSetup]` calling `GameWorldFixture.Build()` and storing whatever subset of the returned `Fixture` it needs (`world`, `previousTime` read from `logic.VisualState.Time.CurrentTime`, `registry`, `resourceIdUpdateOrder`, `firstOrgId`/`firstCountryId`).
- Confirms during implementation (not pre-derived in this plan) that its chosen inputs actually exercise real work — e.g. `ResourceSystemBenchmarks.Update_MonthBoundary`'s date pair must land on a real month transition; `CountryPopulationCollectorBenchmarks` must pick a `countryId` that actually owns provinces in the fixture. Reading each collector's/system's current source before finalizing its benchmark method is part of the implementation step. The isolated correctness tests in the Tests section below turn this "confirm" step into an automated, repeatable check rather than a one-time manual read.

### 5. Result model and comparison (no BenchmarkDotNet JSON exporter dependency)

BenchmarkDotNet's `BenchmarkRunner`/`BenchmarkSwitcher` return `Summary` objects in-process; this harness reads `Summary.Reports` directly instead of round-tripping through BenchmarkDotNet's own exported JSON file layout (simpler, and keeps the snapshot schema entirely under this project's control):

```csharp
// BenchmarkSnapshot.cs
class BenchmarkEntry {
	public string Name = "";              // e.g. "FullTickBenchmarks.Tick_MonthBoundary"
	public double MeanNanoseconds;
	public double StdDevNanoseconds;
	public long AllocatedBytes;
}

class BenchmarkRunRecord {
	public DateTime Timestamp;
	public string Mode = "";              // "compare" | "update-baseline"
	public List<BenchmarkEntry> Results = new();
	public Dictionary<string, bool> Verdicts = new();  // per-benchmark pass/fail; absent key = "new, no baseline"
	public bool Pass;                                   // overall
}
```

- `Program.cs` builds a `BenchmarkDotNet.Configs.ManualConfig` with `MemoryDiagnoser.Default` and (when `--benchmark <name>` is given) a name filter passed to `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config)`; the returned `Summary[]` (one per benchmark class actually run) is flattened into `BenchmarkEntry` records via each `Summary.Reports[i].BenchmarkCase`/`.ResultStatistics.Mean`/`.StandardDeviation` and `.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase)`.
- `SnapshotStore.cs`: `LoadBaseline()`/`SaveBaseline(entries, filteredNamesOnly)` (merge-by-name when a `--benchmark` filter was active — untouched baseline entries for other benchmarks are preserved), `AppendHistory(record)` (append-only, `Docs/Benchmarks/history.json` deserialized as a list, one record appended, rewritten whole — same pattern `eval_history.json` already establishes), `RewriteSummary(record, baseline)` (`Docs/Benchmarks/summary.md`, table format mirroring `Docs/BotFeatures/<featureId>/eval_summary.md`: benchmark name, baseline mean, current mean, % change, pass/fail, allocated bytes — plus a fixed header paragraph stating the cross-machine-comparability caveat verbatim from the spec).
- Gate: `entry.MeanNanoseconds <= baselineEntry.MeanNanoseconds * (1 + epsilonRelative)`, default `epsilonRelative = 0.05`, overridable via `--epsilon <value>`. A benchmark name absent from `baseline.json` is recorded as `Verdicts[name] = true` with a `"new — no baseline"` note in the summary table, never as a failure.

### 6. CLI (`Program.cs`)

```
dotnet run --project src/Game.Benchmarks -c Release -- --compare [--benchmark <name>] [--epsilon <value>]
dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline [--benchmark <name>]
```

- Exactly one of `--compare`/`--update-baseline` required; anything else (missing mode, unknown flag) prints usage to stderr and exits `2`.
- `--benchmark <name>` is a case-insensitive substring match against the benchmark class name (e.g. `ControlSystem`, `CountryPopulation`) — `all`/omitted runs everything.
- `--compare` exit code: `0` iff every benchmark **with a baseline entry** passed its gate; otherwise `1`, with failing benchmark names and their %-slower figures on stderr (mirroring `Game.Evals`' "exit code is the verdict" contract, so this is usable directly as a Ralph task `gate`).
- `--update-baseline` exit code: `0` on a successful run (always — there is no gate to fail; it is an explicit accept action), non-zero only on an execution error (e.g. benchmark threw, filter matched nothing).
- Both modes always append to `history.json` and rewrite `summary.md`; only `--update-baseline` touches `baseline.json`.

### 7. Seeding the initial committed baseline

The harness needs a real, non-fabricated starting baseline. As an implementation step (not by hand-editing JSON): after the project builds in Release, run `dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline` once for real against this plan's own (functionally unmodified — no system/collector logic changes in this plan) code, producing the actual first `Docs/Benchmarks/baseline.json` + first `history.json` entry + `summary.md`, all committed.

### 8. `/benchmark-report` skill (`.claude/commands/benchmark-report.md`)

`$ARGUMENTS` optional, one recognized value: `update-baseline`.

1. Build Release: `dotnet build src/GlobalStrategy.Core.sln -c Release`.
2. If `$ARGUMENTS == "update-baseline"`: run `dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline`; else run `... -- --compare`.
3. Read the just-rewritten `Docs/Benchmarks/summary.md` and present its table directly in chat.
4. No source edits, no `git add`/commit, no PR — purely informational, human decides what (if anything) to do next.

### 9. `/optimize-performance` skill (`.claude/commands/optimize-performance.md`)

`$ARGUMENTS` = a target: a benchmark class-name substring (e.g. `CountryPopulationCollector`, `FullTick`) or `all`.

1. **Authority statement (top of file, mirroring `/implement-bot-feature`'s):** sanctioned autonomous path for **micro-optimizations to already-implemented `src/` game-core logic only** — algorithmic/allocation changes inside an existing system's/collector's `Update`/`Compute`/query method and the pure-C# helpers it calls, gated by `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`'s harness plus the full test suite. Anything that changes observable gameplay behavior, a public signature other callers aren't updated for, ECS component/archetype shape not strictly required by the perf change, or touches `Assets/` is **out of authority** — stop and direct the user to `/specify` + `/plan`.
2. **Branch:** clean tree required (`git status`, never `-uall`); create/switch to `ralph/perf_<target>`.
3. **Write `.ralph/prd.md` directly** (no `/specify`/`/plan`/`/create-prd`), reset `.ralph/activity.md` to its header. One task, `category: "perf-optimization"`:
   - `gate`: `dotnet test src/GlobalStrategy.Core.sln && dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark <target>` (`--benchmark all` when target is `all`) — both must pass; never a fabricated always-green gate.
   - `steps` (written literally into the task, filling in `<target>` and `baseAttempt` = the current attempt count already in `Docs/Benchmarks/history.json` for this target before this run, `0` if none yet):
     1. Run the gate.
     2. Pass → `passes: true`, stop iterating.
     3. Fail → read `Docs/Benchmarks/history.json`'s latest relevant entries + `.ralph/activity.md`; pick **one** concrete optimization attempt to the target's code; journal it and why; re-run.
     4. If attempts since `baseAttempt` reach `5` and the gate still fails: journal budget exhaustion, leave `passes: false`, stop — independent of the Ralph driver's own `-MaxIterations`.
4. **Commit the scaffolding** (branch + PRD + reset activity) via **/commit** before handing off, matching `/implement-bot-feature`'s step 5.
5. **Hand off** — print (does not run itself): `.\scripts\ralph.ps1 -PerfTarget <target>`.
6. **Finish/failure semantics (executed by the driver's phases):** all gates pass → driver runs `--update-baseline` for the target, `/commit`, then `/complete-prd perf:<target>` — PR body carries the before/after benchmark table for the target; the skill/loop **never merges**. Budget exhausted → no baseline update, no PR, branch/activity/history left as-is for a human — identical failure posture to `/implement-bot-feature`.

### 10. Ralph driver mode (`scripts/ralph.py`, `scripts/ralph.ps1`)

Parallel to the existing `--bot-feature`/`-BotFeature` mode (**a mode on the existing scripts, not a sibling script**):

- `ralph.ps1`: new `[string]$PerfTarget` param; validation becomes "exactly one of `-Spec`/`-BotFeature`/`-PerfTarget`"; forwards `--perf-target $PerfTarget` to `ralph.py`.
- `ralph.py`: new `--perf-target` arg; exactly one of `--spec`/`--bot-feature`/`--perf-target` must be given (generalizes the existing pairwise check now that there are three mutually exclusive modes); perf mode: `ralph_branch = f"ralph/perf_{target}"`, `csv_file = Path(f".ralph/metrics_perf_{target}.csv")`, skip spec-folder resolution and `/create-prd` (require `.ralph/prd.md` to already exist with an open task, same as bot mode), phase 3 = `/complete-prd perf:{target}`, incomplete-path failure report names `Docs/Benchmarks/history.json` + `Docs/Benchmarks/summary.md` + `.ralph/activity.md`.
- Shared `$loopTools`/loop-tool allowlist already includes `Bash(dotnet run:*)`/`PowerShell(dotnet run *)` from the bot-feature work — no change needed there; verify `dotnet build`/`dotnet test` are already allowed.

### 11. `/complete-prd` extension (`.claude/commands/complete-prd.md`)

`$ARGUMENTS` gains a third form, `perf:<target>`, alongside the existing spec-index and `bot:<featureId>` forms:

- Spec folder for the report: the standing `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`.
- PR body's Ralph run section is followed by a `## Benchmark verdict` section sourced from `Docs/Benchmarks/history.json` (latest entry for the target) + `summary.md`: per-benchmark before/after mean, % change, pass/fail, attempts, link to `Docs/Benchmarks/`.
- Same rule as the `bot:` form: never fabricate the verdict — read it from the committed files; say so if missing rather than inventing numbers.

*Note found during planning, not fixed by this plan:* `complete-prd.md`'s current `bot:<featureId>` documentation names the standing spec as `Docs/Specs/52_bot-feature-eval-harness/`, a stale pre-rename reference (the actual folder is `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`). Out of scope here — flagged for a future small fix, not touched by this plan to avoid unrelated scope creep.

### 12. Constitution amendment (`Docs/Constitution.md`, Planning Discipline)

Append one bullet under **Planning Discipline**, immediately after the existing **Bot-feature carve-out** bullet:

> - **Performance-optimization carve-out.** Performance-optimization attempts made via `/optimize-performance` — algorithmic/allocation changes to an existing system's/collector's `Update`/`Compute`/query method and the pure-C# helpers it calls, gated by the BenchmarkDotNet harness plus the full test suite — use the skill's directly-written PRD plus the committed `Docs/Benchmarks/history.json` as their planning artifact, under the standing spec/plan pair `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`. Everything outside that narrow surface — including the harness project itself and the two skills' own markdown files — still requires its own approved plan.

### 13. What deliberately does NOT change

- No ECS component, `[Savable]` state, or `VisualState` exposure for anything benchmark-related.
- No change to any system's or collector's public signature or observable behavior — this plan only widens `Program.BuildContext`'s visibility and adds new files.
- `/create-prd`, `.ralph/PROMPT.md`, `.ralph/prd.md` template — unchanged; only the driver scripts and `/complete-prd` gain the perf mode.
- Anything under `Assets/` — the only side effect is the usual Core DLL refresh from rebuilding `src/` in Release (`Game.Benchmarks`, like `Game.Evals`/`Game.ConsoleRunner`, never ships to `Assets/Plugins/Core/`).
- `Docs/BotFeatures/`, the eval harness, `/implement-bot-feature`, and the resource-collector pipeline/recruits features themselves — this plan only *consumes* their public surface, never modifies them.

## Steps

### Agent Steps

- [ ] **Dependency preflight** — Verify `Docs/Specs/26_07_18_17_resource-collector-pipeline/` has landed with the exact shapes this plan assumes (`IResourceCollector`, `ResourceCollectorRegistry`, the ordered `ResourceSystem.Update` overload, trimmed `CountryScoreSystem`, updated `OrgScoreSystem.Recompute`). Stop and reconcile with the user if not. Separately check whether `Docs/Specs/26_07_18_15_recruits-resource/` has also landed (`RecruitsSeedCollector`/`RecruitsGrowthCollector` present or not) — this only determines whether the two recruits benchmark files are included, per the "Hard dependency" section's soft-dependency note.

- [ ] **Create `src/Game.Benchmarks` project** — csproj per §1, added to `src/GlobalStrategy.Core.sln` (new GUID + Debug/Release config lines mirroring the `Game.Evals` entry).

- [ ] **Widen `Program.BuildContext` to `public`** — one-line change in `src/Game.ConsoleRunner/Program.cs`; confirm `dotnet build src/GlobalStrategy.Core.sln` still succeeds.

- [ ] **Write `GameWorldFixture.cs`** — per §2: `Program.BuildContext` + `GameLogic` + one `Update(24f)` call + `GetMatchingArchetypes` scan for the `GameTime` singleton entity + a standalone `ResourceCollectorRegistry.CreateDefault(...)` built from the same committed `game_settings.json`, returning the full `Fixture` (logic, gameTimeEntity, registry, resourceIdUpdateOrder, firstOrgId, firstCountryId).

- [ ] **Write the benchmark classes** — per §4's table (10 files, or 12 if the recruits plan has landed), each `[MemoryDiagnoser]` with its own `[GlobalSetup]` calling `GameWorldFixture.Build()`. `FullTickBenchmarks` additionally needs `[IterationSetup]` per §2/§4. For each collector/system, read its current source first to confirm the chosen inputs exercise real work.

- [ ] **Write the result model, snapshot store, and comparison logic** — `BenchmarkSnapshot.cs`, `SnapshotStore.cs` (load/merge-save baseline, append history, rewrite summary with the comparability caveat in its header) per §5.

- [ ] **Write `Program.cs`** — Debug-build guard, CLI parsing (`--compare`/`--update-baseline`/`--benchmark`/`--epsilon`), `BenchmarkSwitcher` invocation, `Summary.Reports` → `BenchmarkEntry` mapping, gate evaluation, exit codes per §6.

- [ ] **Seed the real initial baseline** — per §7.

- [ ] **Write `/benchmark-report`** — `.claude/commands/benchmark-report.md` per §8.

- [ ] **Write `/optimize-performance`** — `.claude/commands/optimize-performance.md` per §9.

- [ ] **Add the perf mode to the Ralph driver** — `scripts/ralph.ps1`/`ralph.py` per §10. Verify existing `-Spec`/`-BotFeature` behavior is byte-for-byte unchanged.

- [ ] **Extend `/complete-prd`** — `.claude/commands/complete-prd.md` per §11.

- [ ] **Amend the Constitution** — append the §12 bullet verbatim, directly after the bot-feature carve-out bullet.

- [ ] **Release build and DLL refresh** — `dotnet build src/GlobalStrategy.Core.sln -c Release`; confirm `Assets/Plugins/Core/` picks up the refresh and `Game.Benchmarks`'s own output does not land there.

- [ ] **Full-suite sanity run** — `--compare` immediately after seeding the baseline must report all benchmarks passing with `%change` near zero; `--compare --benchmark CountryPopulationCollector` must run only that class; `--compare --benchmark doesNotExist` must report "no benchmarks matched" and a clear non-zero exit.

- [ ] **Debug-run rejection check** — `dotnet run --project src/Game.Benchmarks -- --compare` (no `-c Release`) must print the Release-only error and exit `2` without invoking BenchmarkDotNet.

### User Steps

### 1. Confirm a clean Unity import

Let Unity reload the rebuilt `Assets/Plugins/Core/*.dll` and check `read_console(types=["error"])` — no Unity-side source or asset changed, so only the DLL refresh should be visible.

### 2. Approve the Constitution amendment text

Review the new Planning Discipline bullet in `Docs/Constitution.md` (§12 wording) — a governance change, read rather than skim.

### 3. Review the seeded baseline numbers

Skim `Docs/Benchmarks/summary.md` and `baseline.json` — confirm nothing looks obviously broken (e.g. a benchmark reporting `0ns`, or `CountryPopulationCollector` reporting a suspiciously flat cost that suggests it silently matched zero provinces).

### 4. Dry-run `/optimize-performance` end to end (optional, time-permitting)

Run `/optimize-performance CountryPopulationCollector` — a real, already-flagged optimization target — let it scaffold branch/PRD, then run `.\scripts\ralph.ps1 -PerfTarget CountryPopulationCollector` from a terminal and review the resulting PR's benchmark verdict section before deciding on merge.

## Tests

Test project: `src/Game.Tests/` (xunit). BenchmarkDotNet's own timing runs are explicitly **not** covered by `dotnet test` — this section covers (a) the harness's own comparison/persistence logic on synthetic data, and (b) isolated ECS-world correctness checks proving each benchmark's inputs exercise real, non-degenerate work — the thing BenchmarkDotNet itself cannot verify, since a silently-broken no-op benchmark still reports a (fast, misleading) time.

Adding `src/Game.Benchmarks` as a `ProjectReference` from `src/Game.Tests/Game.Tests.csproj` (same pattern as the existing `Game.Evals` reference):

- **New `src/Game.Tests/BenchmarkGateTests.cs`:**
  - `mean_at_or_below_epsilon_threshold_passes` / `mean_above_epsilon_threshold_fails` — inclusive boundary at `meanBaseline × (1 + epsilonRelative)`.
  - `benchmark_with_no_baseline_entry_auto_passes_as_new` — a name absent from `baseline.json` never fails the overall verdict.
  - `custom_epsilon_overrides_default` — a looser/tighter `--epsilon` value changes the pass/fail boundary for the same synthetic mean pair.
- **New `src/Game.Tests/BenchmarkSnapshotStoreTests.cs`:**
  - `update_baseline_with_filter_only_overwrites_filtered_entries` — a `--benchmark <name>`-scoped `--update-baseline` leaves other benchmarks' baseline entries untouched.
  - `history_append_never_rewrites_earlier_entries` — two sequential appends leave the first entry byte-identical.
  - `summary_rewrite_includes_comparability_caveat_header` — the fixed caveat paragraph is present verbatim.
- **New `src/Game.Tests/BenchmarkFixtureCorrectnessTests.cs`** (the "isolated ECS tests" this plan adds beyond the original spec's ask) — small, hand-built `World`s per the same isolated-fixture style `ResourceSystemTests`/`MultiOrgTestSupport` already use throughout `Game.Tests` (not the full 163-country `GameWorldFixture`, which is exercised only by the real BenchmarkDotNet runs themselves) — each test asserts that the exact call shape a benchmark method uses actually produces a non-trivial state change, catching the class of bug where a benchmark silently measures a no-op:
  - `resource_system_month_boundary_call_shape_actually_triggers_collector_resolve` — build a minimal world with one country-owned `country_population` resource plus a `Monthly` `ResourceCollector`-tagged effect and a stub `IResourceCollector`; call `ResourceSystem.Update` with the exact "last day of month → first day of next month" pair `ResourceSystemBenchmarks.Update_MonthBoundary` uses; assert the stub's `Compute` was actually invoked and the resource's value changed. A regression here means the benchmark's month-boundary variant is quietly measuring the cheap fallback-only path instead.
  - `resource_system_regular_day_call_shape_does_not_trigger_monthly_collectors` — same setup, `Update_RegularDay`'s exact date pair; assert the stub's `Compute` was **not** invoked — confirms the two variants are actually measuring two different things, not the same path twice.
  - `country_population_collector_benchmark_country_owns_at_least_one_province` — the `countryId` `CountryPopulationCollectorBenchmarks` resolves from the fixture must own ≥1 province in the real config data (`Assets/Configs/country_config.json` + `province_config.json`); assert this against the actual committed config rather than assuming it — a country with zero provinces would make this benchmark measure an empty loop, defeating its entire purpose (watching the flagged O(countries × provinces) cost).
  - `full_tick_iteration_setup_reliably_forces_month_boundary` — construct a `GameTime` at the fixed pre-iteration date `FullTickBenchmarks.Tick_MonthBoundary`'s `[IterationSetup]` uses, advance by the benchmarked `24f`, assert the resulting date is genuinely in the next month (guards against an off-by-one in the fixed date picked, e.g. accidentally using a 30-day month's day 31).
  - `full_tick_iteration_setup_reliably_avoids_month_boundary` — same, for `Tick_RegularDay`'s fixed date: advancing by `24f` must land in the same month.

Run: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts — with the note that this plan itself introduces one amendment, per the spec's explicit ask.**

- *ECS for all game logic in `src/`.* No new ECS component; the harness only calls existing public `Update`/`Compute`/query methods from outside `Game.Main`/`Game.Systems`, reading a singleton entity id via the documented `GetMatchingArchetypes` pattern. No MonoBehaviour, no Unity-side logic.
- *VContainer sole DI.* No Unity-side registration touched.
- *UI Toolkit only / URP only.* No UI, rendering, or `Assets/` change of any kind (Core DLL refresh excepted).
- *Planning Discipline — plan before implement.* This plan implements the approved spec, and takes a hard dependency on `Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md` landing first (Correction #2, above) rather than building against systems already slated for deletion. **Amendment introduced by this plan:** the §12 carve-out bullet, required by the spec's own acceptance criteria, is added so `/optimize-performance`-driven changes (PRD + benchmark history under this standing spec/plan) keep the written principle literally true.
- *Specification Discipline.* This spec preceded this plan; per-target optimization work is covered by the standing spec + the new carve-out, exactly as the spec's Resolved Decisions require.
- *File Organisation.* This plan lives at `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/plan.md`, paired with its spec under the same dated identifier. `Docs/Benchmarks/` is a new, distinct documentation home — same non-colliding pattern `Docs/BotFeatures/` already established.
- *One `.asmdef` per feature folder under `Assets/Scripts/`.* No `Assets/Scripts` folder or asmdef touched; new code lands entirely in the new `src/Game.Benchmarks` csproj (net8.0, sln-only, never in Plugins).
- *C# code style.* Tabs, braces always, `_`-prefixed privates, no redundant access modifiers throughout the new code, matching `Game.Systems`/`Game.Evals`/`Game.Tests`.

Use /implement to start working on the plan or request changes.
