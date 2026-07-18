# Plan: BenchmarkDotNet Performance Harness & Optimization Skills

## Spec

Source: `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/spec.md`.

**Intent.** A BenchmarkDotNet-based harness measuring the full per-tick `GameLogic.Update` cost and named hot ECS systems, committed baseline snapshots under `Docs/Benchmarks/` so regressions/improvements are visible in git history, plus two skills: `/benchmark-report` (human-driven compare/report) and `/optimize-performance` (bounded Ralph-loop autonomous optimization with a Constitution carve-out), mirroring `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`'s shape for wall-clock/allocation performance instead of gameplay score.

**Resolved decisions (from `/specify`, binding):**
- Benchmark both the full tick and individual hot systems (not one or the other).
- Snapshots committed under `Docs/Benchmarks/` (baseline + append-only history + rewritten summary) — never gitignored.
- Both skills are in scope: `/benchmark-report` (no autonomy) and `/optimize-performance` (bounded Ralph loop, ends in PR).

**Correction to the spec found during planning.** The spec's "What is benchmarked" section states `OrgScoreSystem`'s scoring "is not ticked every frame inside `GameLogic.Update`." That is incorrect — `GameLogic.Update` calls `OrgScoreSystem.Update(_world, _previousTime, currentTime)` unconditionally every tick (`src/Game.Main/GameLogic.cs:164`), immediately after `CreateActionEffectSystem`. Separately, `OrgScoreSystem.GetScore(IReadOnlyWorld world, string orgId)` is a distinct derived query the eval harness calls once per org per sample (`src/Game.ConsoleRunner/HeadlessRunner.cs:197`). This plan benchmarks **both**: `OrgScoreSystem.Update` joins the individually-benchmarked per-tick systems list (structurally identical to the other five — no config file, deterministic, called every tick), and `OrgScoreSystem.GetScore` gets its own separate benchmark for the reason the spec actually cared about (bounding eval-batch wall-clock time). This does not change the spec's intent, only which method name maps to which claim.

**Key acceptance criteria (design targets):**
- New net8.0 exe `src/Game.Benchmarks`, added to `src/GlobalStrategy.Core.sln`, referencing BenchmarkDotNet; never emitted to `Assets/Plugins/Core/`; no `Assets/` change of any kind. Must be run as `dotnet run --project src/Game.Benchmarks -c Release -- <args>` — Debug runs are rejected with a clear error.
- Fixture built once per benchmark class via a shared helper reusing the existing config-loading/bootstrap path (`Program.BuildContext` + `GameLogicContext` + `GameLogic`), producing the real, fully-populated 163-country/province/org world — not a hand-built minimal fixture.
- Benchmarked: full `GameLogic.Update(deltaTime)` tick (idle, no queued commands, `deltaTime` guaranteed to cross the hour-accumulation threshold); `TimeSystem`, `ResourceSystem`, `ControlSystem`, `ProvincePopulationGrowthSystem`, `CountryScoreSystem`, `OrgScoreSystem.Update` individually; `OrgScoreSystem.GetScore` individually. `[MemoryDiagnoser]` on every class.
- Snapshots: `Docs/Benchmarks/baseline.json` (current accepted numbers), `Docs/Benchmarks/history.json` (append-only, one entry per `--compare`/`--update-baseline` run, never rewritten), `Docs/Benchmarks/summary.md` (rewritten every run, documents the cross-machine-comparability caveat in its header).
- `--compare` mode: runs the suite (or a `--benchmark <name>` filtered subset), gates each benchmark with an existing baseline entry on `meanCurrent ≤ meanBaseline × (1 + epsilonRelative)` (default `0.05`), exit code `0` iff every gated benchmark passes; a benchmark with no baseline entry auto-passes ("new — no baseline"). Allocated bytes reported, never gated in v1.
- `--update-baseline` mode: runs the suite (optionally filtered), always appends to history + rewrites summary, then overwrites the (filtered subset of) `baseline.json` — an explicit, separate action from `--compare`.
- `/benchmark-report` skill: runs `--compare` (or `--update-baseline` when `$ARGUMENTS == "update-baseline"`), shows the table in chat; no source edits, no commit, no PR.
- `/optimize-performance <target>` skill: mirrors `/implement-bot-feature`'s shape — authority statement scoping it to micro-optimizations of already-implemented `src/` game-core logic; branch `ralph/perf_<target>`; writes `.ralph/prd.md` directly with a gate of `dotnet test src/GlobalStrategy.Core.sln && dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark <target>`; bounded attempt budget (default 5) reading `Docs/Benchmarks/history.json` + `.ralph/activity.md` between attempts; on success runs `--update-baseline`, commits, hands off to `/complete-prd perf:<target>`; on budget exhaustion, no baseline update, no PR, everything left for a human.
- Constitution amendment: a second Planning Discipline carve-out (alongside the existing bot-feature one) for `/optimize-performance`-driven changes within its authority statement, standing spec/plan = this pair.

**Out of scope (unchanged from spec):** CI integration, cross-machine baseline normalization, Unity/Assets profiling, allocation-based gating, automatic runtime revert-on-regression, a genetic/statistical optimizer, benchmarking startup/save-load (deferred to a follow-up spec).

## Goal

Add `src/Game.Benchmarks` (net8.0 BenchmarkDotNet host) with one fixture builder and seven `[Benchmark]`-bearing classes (full tick + six named systems/queries) plus a `--compare`/`--update-baseline` CLI built on BenchmarkDotNet's in-process `Summary` results (no dependency on BenchmarkDotNet's own JSON exporter file layout); widen `Game.ConsoleRunner.Program.BuildContext` from `internal` to `public` (the one production-code visibility change this plan needs); seed `Docs/Benchmarks/` with a real initial baseline/history/summary produced by an actual `--update-baseline` run against the current `main`-equivalent code (i.e., this plan's own unmodified systems — establishing "today's numbers" as the starting baseline, not a fabricated one); add `.claude/commands/benchmark-report.md` and `.claude/commands/optimize-performance.md`; extend `scripts/ralph.py`/`ralph.ps1` with a `--perf-target`/`-PerfTarget` mode paralleling the existing `--bot-feature`/`-BotFeature` mode; extend `.claude/commands/complete-prd.md` for a `perf:<target>` argument form; amend `Docs/Constitution.md`'s Planning Discipline section with the performance carve-out. All new code is `src/`-side or `.claude`/`scripts`/`Docs`-side; no Unity asset, scene, or script changes; the only Unity-visible side effect is the usual `Assets/Plugins/Core/` DLL refresh from rebuilding `src/` in Release.

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

**Release-only guard.** `Program.Main` reads `#if DEBUG` (compile-time, reliable — no reflection on `IsAssignableFrom(typeof(DebuggableAttribute))` needed) and, if defined, prints `"Game.Benchmarks must be run with -c Release (BenchmarkDotNet requires a Release build for valid measurements)."` to stderr and exits `2` before touching BenchmarkDotNet at all.

### 2. Shared fixture builder (`GameWorldFixture.cs`)

```csharp
namespace GS.Game.Benchmarks {
	static class GameWorldFixture {
		public const string ConfigDir = "Assets/Configs";
		public const int Seed = 1880;

		public static (GameLogic Logic, int GameTimeEntity) Build() {
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
			return (logic, gameTimeEntity);
		}
	}
}
```

- Requires `Game.ConsoleRunner.Program.BuildContext` to be `public` (see §3). Reuses it directly rather than duplicating config-loading — the one production change this plan needs.
- `24f` matches `HeadlessRunner`'s own `deltaTime = hoursPerTick / speedMultipliers[0]` convention (`options.HoursPerTick` default 24, `speedMultipliers[0] == 1` per `Assets/Configs/game_settings.json`) — one `Update` call advances exactly one day and (this being the very first call) also runs `InitSystem`, so the returned `logic.World` is the real, fully-populated 163-country/province/org world at day 2 of the configured start year. No hand-built minimal world.
- The `GetMatchingArchetypes` scan for the singleton `GameTime` entity is the documented pattern from `.claude/rules/unity/ecs_patterns.md` ("Singleton entities") for exactly this situation — code outside `GameLogic` that needs a singleton's entity id and has no cached one. It runs once per benchmark class's `[GlobalSetup]`, not per iteration.
- Each `[Benchmark]` class calls `GameWorldFixture.Build()` independently in its own `[GlobalSetup]` (BenchmarkDotNet does not share state across classes) — cost is amortized (excluded from measurement) and each class benchmarks against its own untouched-by-other-classes world instance.
- **No per-iteration reset.** Systems benchmarked here (`ResourceSystem`, `ControlSystem`, `ProvincePopulationGrowthSystem`, `CountryScoreSystem`, `OrgScoreSystem.Update`) iterate a fixed number of ECS entities (countries/provinces/orgs) per call; their cost is dominated by entity counts, not by the numeric magnitude of the values they mutate (gold, control, population, score). Letting values compound across a benchmark run's many iterations (with no `[IterationSetup]` reset) does not skew the measured per-call cost and avoids paying full fixture-rebuild cost thousands of times per run. `TimeSystem`'s `AccumulatedHours` is bounded by its own per-call `deltaTime` (see `.claude/rules/unity/ecs_patterns.md`'s fractional-accumulation pattern) and never grows unbounded. This is a benchmark-design note to carry into implementation, not something requiring new code.

### 3. `Program.BuildContext` visibility (`src/Game.ConsoleRunner/Program.cs`)

One-line change: `internal static GameLogicContext BuildContext(...)` → `public static GameLogicContext BuildContext(...)`. Purely additive visibility widening (no behavior change, no signature change); `Game.Evals` and `RunInteractive` keep calling it exactly as before. This is the only change to existing production code in this plan.

### 4. Benchmark classes (one file per benchmarked thing, mirroring `Game.Systems`' one-system-per-file convention)

| File | `[Benchmark]` | What it calls | `previousTime`/`currentTime` |
|---|---|---|---|
| `FullTickBenchmarks.cs` | `Tick()` | `logic.Update(24f)` | n/a (advances 1 day per call) |
| `TimeSystemBenchmarks.cs` | `Update()` | `TimeSystem.Update(world, gameTimeEntity, 24f, speedMultipliers, default, default, default)` | n/a |
| `ResourceSystemBenchmarks.cs` | `Update()` | `ResourceSystem.Update(world, previousTime, previousTime.AddDays(1))` | +1 day |
| `ControlSystemBenchmarks.cs` | `Update()` | `ControlSystem.Update(world, previousTime, previousTime.AddDays(1))` | +1 day |
| `ProvincePopulationGrowthSystemBenchmarks.cs` | `Update()` | `ProvincePopulationGrowthSystem.Update(world, previousTime, previousTime.AddMonths(1), growthPercent)` | +1 month (matches its monthly-growth contract) |
| `CountryScoreSystemBenchmarks.cs` | `Update()` | `CountryScoreSystem.Update(world, previousTime, previousTime.AddMonths(1), coefficient)` | +1 month |
| `OrgScoreSystemBenchmarks.cs` | `UpdateTick()`, `GetScore()` | `OrgScoreSystem.Update(world, previousTime, previousTime.AddDays(1))`; `OrgScoreSystem.GetScore(world, orgId)` (first org id from the fixture's participating list) | +1 day for `Update`; n/a for the pure query |

Each class:
- Is `[MemoryDiagnoser]`.
- Has a `[GlobalSetup]` calling `GameWorldFixture.Build()`, storing `logic`, `world = logic.World`, `previousTime = logic.VisualState.Time.CurrentTime` (or the equivalent already-public read), and system-specific constants (`growthPercent`/`coefficient` read from the same `GameSettings` the fixture already loaded — expose them from `GameWorldFixture.Build()`'s context, no new `GameLogic` API needed since `GameLogicContext.GameSettings` is a public loader already).
- Confirms during implementation (not pre-derived in this plan) that the chosen `previousTime`/`currentTime` gap actually exercises that system's real branch — e.g. `ProvincePopulationGrowthSystem`'s monthly gate must see the currentTime cross a month boundary, not just any positive gap. Reading each system's source before writing its benchmark method is part of the implementation step, not assumed here.

### 5. Result model and comparison (no BenchmarkDotNet JSON exporter dependency)

BenchmarkDotNet's `BenchmarkRunner`/`BenchmarkSwitcher` return `Summary` objects in-process; this harness reads `Summary.Reports` directly instead of round-tripping through BenchmarkDotNet's own exported JSON file layout (simpler, and keeps the snapshot schema entirely under this project's control):

```csharp
// BenchmarkSnapshot.cs
class BenchmarkEntry {
	public string Name = "";              // e.g. "FullTickBenchmarks.Tick"
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

- `Program.cs` builds a `BenchmarkDotNet.Configs.ManualConfig` with `MemoryDiagnoser.Default` and (when `--benchmark <name>` is given) an `NameFilter`/`--filter *<name>*` equivalent passed to `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config)`; the returned `Summary[]` (one per benchmark class actually run) is flattened into `BenchmarkEntry` records via each `Summary.Reports[i].BenchmarkCase`/`.ResultStatistics.Mean`/`.StandardDeviation` and `.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase)`.
- `SnapshotStore.cs`: `LoadBaseline()`/`SaveBaseline(entries, filteredNamesOnly)` (merge-by-name when a `--benchmark` filter was active — untouched baseline entries for other benchmarks are preserved), `AppendHistory(record)` (append-only, `Docs/Benchmarks/history.json` deserialized as a list, one record appended, rewritten whole — same pattern `eval_history.json` already establishes), `RewriteSummary(record, baseline)` (`Docs/Benchmarks/summary.md`, table format mirroring `Docs/BotFeatures/<featureId>/eval_summary.md`: benchmark name, baseline mean, current mean, % change, pass/fail, allocated bytes — plus a fixed header paragraph stating the cross-machine-comparability caveat verbatim from the spec).
- Gate: `entry.MeanNanoseconds <= baselineEntry.MeanNanoseconds * (1 + epsilonRelative)`, default `epsilonRelative = 0.05`, overridable via `--epsilon <value>`. A benchmark name absent from `baseline.json` is recorded as `Verdicts[name] = true` with a `"new — no baseline"` note in the summary table, never as a failure.

### 6. CLI (`Program.cs`)

```
dotnet run --project src/Game.Benchmarks -c Release -- --compare [--benchmark <name>] [--epsilon <value>]
dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline [--benchmark <name>]
```

- Exactly one of `--compare`/`--update-baseline` required; anything else (missing mode, unknown flag) prints usage to stderr and exits `2`.
- `--benchmark <name>` is a case-insensitive substring match against the benchmark class name (e.g. `ControlSystem`, `FullTick`) — `all`/omitted runs everything.
- `--compare` exit code: `0` iff every benchmark **with a baseline entry** passed its gate; otherwise `1`, with failing benchmark names and their %-slower figures on stderr (mirroring `Game.Evals`' "exit code is the verdict" contract, so this is usable directly as a Ralph task `gate`).
- `--update-baseline` exit code: `0` on a successful run (always — there is no gate to fail; it is an explicit accept action), non-zero only on an execution error (e.g. benchmark threw, filter matched nothing).
- Both modes always append to `history.json` and rewrite `summary.md`; only `--update-baseline` touches `baseline.json`.

### 7. Seeding the initial committed baseline

The harness needs a real, non-fabricated starting baseline, not an empty scaffold that makes every first `/benchmark-report` report "new — no baseline" for everything. As an implementation step (not by hand-editing JSON): after the project builds in Release, run `dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline` once for real against this plan's own (functionally unmodified — no system logic changes in this plan) code, producing the actual first `Docs/Benchmarks/baseline.json` + first `history.json` entry + `summary.md`, all committed. This documents "today's numbers" honestly rather than inventing placeholder figures.

### 8. `/benchmark-report` skill (`.claude/commands/benchmark-report.md`)

`$ARGUMENTS` optional, one recognized value: `update-baseline`.

1. Build Release: `dotnet build src/GlobalStrategy.Core.sln -c Release`.
2. If `$ARGUMENTS == "update-baseline"`: run `dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline`; else run `... -- --compare`.
3. Read the just-rewritten `Docs/Benchmarks/summary.md` and present its table directly in chat.
4. No source edits, no `git add`/commit, no PR — purely informational, human decides what (if anything) to do next.

### 9. `/optimize-performance` skill (`.claude/commands/optimize-performance.md`)

`$ARGUMENTS` = a target: a benchmark class-name substring (e.g. `ControlSystem`, `FullTick`) or `all`.

1. **Authority statement (top of file, mirroring `/implement-bot-feature`'s):** sanctioned autonomous path for **micro-optimizations to already-implemented `src/` game-core logic only** — algorithmic/allocation changes inside an existing system's `Update`/query method and the pure-C# helpers it calls, gated by `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`'s harness plus the full test suite. Anything that changes observable gameplay behavior, a public system signature other callers aren't updated for, ECS component/archetype shape not strictly required by the perf change, or touches `Assets/` is **out of authority** — stop and direct the user to `/specify` + `/plan`.
2. **Branch:** clean tree required (`git status`, never `-uall`); create/switch to `ralph/perf_<target>`.
3. **Write `.ralph/prd.md` directly** (no `/specify`/`/plan`/`/create-prd`), reset `.ralph/activity.md` to its header. One task, `category: "perf-optimization"`:
   - `gate`: `dotnet test src/GlobalStrategy.Core.sln && dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark <target>` (`--benchmark all` when target is `all`) — both must pass; never a fabricated always-green gate.
   - `steps` (written literally into the task, filling in `<target>` and `baseAttempt` = the current attempt count already in `Docs/Benchmarks/history.json` for this target before this run, `0` if none yet):
     1. Run the gate.
     2. Pass → `passes: true`, stop iterating.
     3. Fail → read `Docs/Benchmarks/history.json`'s latest relevant entries + `.ralph/activity.md`; pick **one** concrete optimization attempt to the target's code; journal it and why; re-run.
     4. If attempts since `baseAttempt` reach `5` and the gate still fails: journal budget exhaustion, leave `passes: false`, stop — independent of the Ralph driver's own `-MaxIterations`.
4. **Commit the scaffolding** (empty PRD isn't committed alone — commit alongside the first attempt's code change once one exists; the initial branch+PRD+reset-activity state is committed via **/commit** before handing off, matching `/implement-bot-feature`'s step 5).
5. **Hand off** — print (does not run itself): `.\scripts\ralph.ps1 -PerfTarget <target>`.
6. **Finish/failure semantics (executed by the driver's phases):** all gates pass → driver runs `--update-baseline` for the target, `/commit`, then `/complete-prd perf:<target>` — PR body carries the before/after benchmark table for the target; the skill/loop **never merges**. Budget exhausted → no baseline update, no PR, branch/activity/history left as-is for a human — identical failure posture to `/implement-bot-feature`.

### 10. Ralph driver mode (`scripts/ralph.py`, `scripts/ralph.ps1`)

Parallel to the existing `--bot-feature`/`-BotFeature` mode (**a mode on the existing scripts, not a sibling script** — same precedent as bot-feature mode):

- `ralph.ps1`: new `[string]$PerfTarget` param; validation becomes "exactly one of `-Spec`/`-BotFeature`/`-PerfTarget`"; forwards `--perf-target $PerfTarget` to `ralph.py`.
- `ralph.py`: new `--perf-target` arg; `(args.spec is None) + (args.bot_feature is None) + (args.perf_target is None)` must equal exactly 2 (exactly one mode selected — generalizes the existing pairwise check now that there are three mutually exclusive modes); perf mode: `ralph_branch = f"ralph/perf_{target}"`, `csv_file = Path(f".ralph/metrics_perf_{target}.csv")`, skip spec-folder resolution and `/create-prd` (require `.ralph/prd.md` to already exist with an open task, same as bot mode), phase 3 = `/complete-prd perf:{target}`, incomplete-path failure report names `Docs/Benchmarks/history.json` + `Docs/Benchmarks/summary.md` + `.ralph/activity.md` (paralleling the bot-mode message that currently names `Docs/BotFeatures/<featureId>/eval_summary.md` + `eval_history.json`).
- Shared `$loopTools`/loop-tool allowlist already includes `Bash(dotnet run:*)`/`PowerShell(dotnet run *)` from the bot-feature work — no change needed there; verify `dotnet build`/`dotnet test` are already allowed (they are, per existing spec-mode usage).

### 11. `/complete-prd` extension (`.claude/commands/complete-prd.md`)

`$ARGUMENTS` gains a third form, `perf:<target>`, alongside the existing spec-index and `bot:<featureId>` forms:

- Spec folder for the report: the standing `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`.
- PR body's Ralph run section is followed by a `## Benchmark verdict` section sourced from `Docs/Benchmarks/history.json` (latest entry for the target) + `summary.md`: per-benchmark before/after mean, % change, pass/fail, attempts, link to `Docs/Benchmarks/`.
- Same rule as the `bot:` form: never fabricate the verdict — read it from the committed files; say so if missing rather than inventing numbers.

*Note found during planning, not fixed by this plan:* `complete-prd.md`'s current `bot:<featureId>` documentation names the standing spec as `Docs/Specs/52_bot-feature-eval-harness/`, a stale pre-rename reference (the actual folder is `Docs/Specs/26_07_16_14_bot-feature-eval-harness/`). Out of scope here — flagged for a future small fix, not touched by this plan to avoid unrelated scope creep.

### 12. Constitution amendment (`Docs/Constitution.md`, Planning Discipline)

Append one bullet under **Planning Discipline**, immediately after the existing **Bot-feature carve-out** bullet:

> - **Performance-optimization carve-out.** Performance-optimization attempts made via `/optimize-performance` — algorithmic/allocation changes to an existing system's `Update`/query method and the pure-C# helpers it calls, gated by the BenchmarkDotNet harness plus the full test suite — use the skill's directly-written PRD plus the committed `Docs/Benchmarks/history.json` as their planning artifact, under the standing spec/plan pair `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`. Everything outside that narrow surface — including the harness project itself and the two skills' own markdown files — still requires its own approved plan.

### 13. What deliberately does NOT change

- No ECS component, `[Savable]` state, or `VisualState` exposure for anything benchmark-related.
- No change to any system's public signature or observable behavior — this plan only widens `Program.BuildContext`'s visibility and adds new files.
- `/create-prd`, `.ralph/PROMPT.md`, `.ralph/prd.md` template — unchanged; only the driver scripts and `/complete-prd` gain the perf mode.
- Anything under `Assets/` — the only side effect is the usual Core DLL refresh from rebuilding `src/` in Release (no `Game.Systems`/`Game.Main` logic actually changes in this plan, so the refreshed DLLs are behaviorally identical; `Game.Benchmarks`, like `Game.Evals`/`Game.ConsoleRunner`, never ships to `Assets/Plugins/Core/`).
- `Docs/BotFeatures/`, the eval harness, and `/implement-bot-feature` — entirely separate skill/harness, untouched.

## Steps

### Agent Steps

- [ ] **Create `src/Game.Benchmarks` project** — csproj per §1, added to `src/GlobalStrategy.Core.sln` (new GUID + Debug/Release config lines mirroring the `Game.Evals` entry).

- [ ] **Widen `Program.BuildContext` to `public`** — one-line change in `src/Game.ConsoleRunner/Program.cs`; confirm `dotnet build src/GlobalStrategy.Core.sln` still succeeds and no existing caller needed adjustment.

- [ ] **Write `GameWorldFixture.cs`** — per §2: `Program.BuildContext` + `GameLogic` + one `Update(24f)` call + `GetMatchingArchetypes` scan for the `GameTime` singleton entity, returning `(GameLogic, int gameTimeEntity)`.

- [ ] **Write the seven benchmark classes** — per §4, one file per row of the table, each `[MemoryDiagnoser]` with its own `[GlobalSetup]` calling `GameWorldFixture.Build()`. For each system, read its current source first to confirm the chosen `previousTime`/`currentTime` gap exercises real work (not a no-op fast path) before finalizing the benchmark method.

- [ ] **Write the result model, snapshot store, and comparison logic** — `BenchmarkSnapshot.cs`, `SnapshotStore.cs` (load/merge-save baseline, append history, rewrite summary with the comparability caveat in its header) per §5.

- [ ] **Write `Program.cs`** — Debug-build guard, CLI parsing (`--compare`/`--update-baseline`/`--benchmark`/`--epsilon`), `BenchmarkSwitcher` invocation, `Summary.Reports` → `BenchmarkEntry` mapping, gate evaluation, exit codes per §6.

- [ ] **Seed the real initial baseline** — per §7: `dotnet build src/GlobalStrategy.Core.sln -c Release`, then `dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline`; confirm `Docs/Benchmarks/baseline.json`/`history.json`/`summary.md` are produced with real numbers (not placeholders) and commit them as part of this feature.

- [ ] **Write `/benchmark-report`** — `.claude/commands/benchmark-report.md` per §8.

- [ ] **Write `/optimize-performance`** — `.claude/commands/optimize-performance.md` per §9 (authority statement, branch, direct PRD with the bounded-attempt gate task, commit, driver hand-off, finish/failure semantics).

- [ ] **Add the perf mode to the Ralph driver** — `scripts/ralph.ps1` (`-PerfTarget` param, three-way exactly-one validation) and `scripts/ralph.py` (`--perf-target` arg, `ralph/perf_<target>` branch, `.ralph/metrics_perf_<target>.csv`, skip spec resolution/`/create-prd`, phase 3 `/complete-prd perf:<target>`, incomplete-path message naming `Docs/Benchmarks/history.json`/`summary.md`) per §10. Verify existing `-Spec`/`-BotFeature` behavior is byte-for-byte unchanged.

- [ ] **Extend `/complete-prd`** — `.claude/commands/complete-prd.md`: `perf:<target>` argument form, standing-spec resolution, `## Benchmark verdict` PR-body section per §11.

- [ ] **Amend the Constitution** — append the §12 bullet verbatim to `Docs/Constitution.md`'s Planning Discipline section, directly after the bot-feature carve-out bullet.

- [ ] **Release build and DLL refresh** — `dotnet build src/GlobalStrategy.Core.sln -c Release`; confirm `Assets/Plugins/Core/` picks up the refresh and `Game.Benchmarks`'/`Game.Evals`'/`Game.ConsoleRunner`'s own outputs do not land there.

- [ ] **Full-suite sanity run** — `dotnet run --project src/Game.Benchmarks -c Release -- --compare` immediately after seeding the baseline (§7's step) must report all seven benchmarks passing (identical code just measured twice) with `%change` near zero; `dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark ControlSystem` must run only the `ControlSystemBenchmarks` class; `--compare --benchmark doesNotExist` must report "no benchmarks matched" and a clear non-zero exit rather than silently passing.

- [ ] **Debug-run rejection check** — `dotnet run --project src/Game.Benchmarks -- --compare` (no `-c Release`) must print the Release-only error and exit `2` without invoking BenchmarkDotNet.

### User Steps

### 1. Confirm a clean Unity import

Let Unity reload the rebuilt `Assets/Plugins/Core/*.dll` and check `read_console(types=["error"])` — no Unity-side source or asset changed, so only the DLL refresh should be visible.

### 2. Approve the Constitution amendment text

Review the new Planning Discipline bullet in `Docs/Constitution.md` (§12 wording) — a governance change, read rather than skim.

### 3. Review the seeded baseline numbers

Skim `Docs/Benchmarks/summary.md` and `baseline.json` — these are the real numbers this plan's harness run produced on this machine; confirm nothing looks obviously broken (e.g. a benchmark reporting `0ns` or an exception swallowed into a zero).

### 4. Dry-run `/optimize-performance` end to end (optional, time-permitting)

Run `/optimize-performance ControlSystem` with a trivial, safe target, let it scaffold branch/PRD, then run `.\scripts\ralph.ps1 -PerfTarget ControlSystem` from a terminal and review the resulting PR's benchmark verdict section before deciding on merge.

## Tests

Test project: `src/Game.Tests/` (xunit). BenchmarkDotNet's own timing runs are explicitly **not** covered by `dotnet test` (they are wall-clock benchmarks, not unit tests) — this section covers the harness's own comparison/persistence logic on synthetic data, following the eval harness's precedent of "no full simulations/benchmarks in unit tests."

Adding `src/Game.Benchmarks` as a `ProjectReference` from `src/Game.Tests/Game.Tests.csproj` (same pattern as the existing `Game.Evals` reference) so its non-benchmark classes (`SnapshotStore`, gate logic) are testable:

- **New `src/Game.Tests/BenchmarkGateTests.cs`:**
  - `mean_at_or_below_epsilon_threshold_passes` / `mean_above_epsilon_threshold_fails` — inclusive boundary at `meanBaseline × (1 + epsilonRelative)`.
  - `benchmark_with_no_baseline_entry_auto_passes_as_new` — a name absent from `baseline.json` never fails the overall verdict.
  - `custom_epsilon_overrides_default` — a looser/tighter `--epsilon` value changes the pass/fail boundary for the same synthetic mean pair.
- **New `src/Game.Tests/BenchmarkSnapshotStoreTests.cs`:**
  - `update_baseline_with_filter_only_overwrites_filtered_entries` — a `--benchmark <name>`-scoped `--update-baseline` leaves other benchmarks' baseline entries untouched.
  - `history_append_never_rewrites_earlier_entries` — two sequential appends leave the first entry byte-identical.
  - `summary_rewrite_includes_comparability_caveat_header` — the fixed caveat paragraph is present verbatim.

Run: `dotnet test src/GlobalStrategy.Core.sln`.

## Constitution Check

Checked against `Docs/Constitution.md`. **No conflicts — with the note that this plan itself introduces one amendment, per the spec's explicit ask.**

- *ECS for all game logic in `src/`.* No new ECS component; the harness only calls existing `Update`/query methods from outside `Game.Main`/`Game.Systems`, reading a singleton entity id via the documented `GetMatchingArchetypes` pattern. No MonoBehaviour, no Unity-side logic.
- *VContainer sole DI.* No Unity-side registration touched.
- *UI Toolkit only / URP only.* No UI, rendering, or `Assets/` change of any kind (Core DLL refresh excepted).
- *Planning Discipline — plan before implement.* This plan implements the approved spec. **Amendment introduced by this plan:** the §12 carve-out bullet, required by the spec's own acceptance criteria, is added so `/optimize-performance`-driven changes (PRD + benchmark history under this standing spec/plan) keep the written principle literally true. Until that amendment lands (as part of this feature), no performance optimization is implemented via the skill.
- *Specification Discipline.* This spec preceded this plan; per-target optimization work is covered by the standing spec + the new carve-out, exactly as the spec's Resolved Decisions require.
- *File Organisation.* This plan lives at `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/plan.md`, paired with its spec under the same dated identifier. `Docs/Benchmarks/` is a new, distinct documentation home — same non-colliding pattern `Docs/BotFeatures/` already established.
- *One `.asmdef` per feature folder under `Assets/Scripts/`.* No `Assets/Scripts` folder or asmdef touched; new code lands entirely in the new `src/Game.Benchmarks` csproj (net8.0, sln-only, never in Plugins).
- *C# code style.* Tabs, braces always, `_`-prefixed privates, no redundant access modifiers throughout the new code, matching `Game.Systems`/`Game.Evals`/`Game.Tests`.

Use /implement to start working on the plan or request changes.
