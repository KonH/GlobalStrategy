# Spec: BenchmarkDotNet Performance Harness & Optimization Skills

## Feature Intent

As a developer growing the ECS game-core logic in `src/`, I want a BenchmarkDotNet-based performance harness that measures the full per-tick `GameLogic.Update` cost and the individual hot ECS systems it drives, saves committed baseline snapshots so regressions and improvements are visible in git history, plus two new skills — a simple compare/report skill for day-to-day use, and an autonomous Ralph-loop optimization skill for bounded, gated performance-improvement attempts — so that performance work has objective, reproducible before/after evidence instead of guesswork or ad-hoc local profiling.

This is a net8.0, `src/`-only initiative, mirroring the shape of `Docs/Specs/26_07_16_14_bot-feature-eval-harness/` (harness project + committed history + a Ralph-loop skill with a Constitution carve-out) but for wall-clock/allocation performance instead of gameplay score.

## Acceptance Criteria

### Benchmark project

- **Given** the need for a dedicated BenchmarkDotNet host **Then** a new **net8.0** executable project `src/Game.Benchmarks` exists, added to `src/GlobalStrategy.Core.sln`, referencing `BenchmarkDotNet` (NuGet) plus whatever `src/` projects it needs to construct a populated `World` (`ECS.Core`, `Game.Main`, `Game.Systems`, `Game.Configs.Loader`, `Game.ConsoleRunner` for its existing config-loading/bootstrap helpers). Like `Game.Evals`/`Game.ConsoleRunner`, it is never emitted to `Assets/Plugins/Core/` and makes no `Assets/`-side change of any kind.
- **Given** BenchmarkDotNet requires a Release build for valid measurements **Then** the project's own README/usage note (or the skill instructions consuming it) states the run command explicitly as `dotnet run --project src/Game.Benchmarks -c Release -- <args>` — never `dotnet test`, never a Debug run — and the harness fails fast with a clear error if launched under a Debug configuration.
- **Given** a representative world is needed for realistic measurements **Then** benchmark classes build their fixture once in `[GlobalSetup]` by reusing the same config-loading/bootstrap path the headless runner already uses (`Game.Configs.Loader` + `GameLogicContext`) to construct a fully-populated `World` (all 163 countries, provinces, orgs from the committed configs) at a fixed simulated start date — no ad-hoc hand-built minimal worlds that don't reflect real data volume, and no per-invocation reconstruction of the world inside the timed `[Benchmark]` method itself.

### What is benchmarked

- **Given** the full per-tick cost **Then** one benchmark class times `GameLogic.Update(deltaTime)` end-to-end against the populated fixture, with no queued player commands (an idle/no-input tick) and a fixed `deltaTime` large enough to guarantee the hour-accumulation systems (per `.claude/rules/unity/ecs_patterns.md`'s fractional-accumulation pattern) actually advance at least one unit of work per call — a tick benchmark that silently no-ops every system because `deltaTime` was too small to cross a threshold is not a valid measurement.
- **Given** the systems `GameLogic.Update` drives every tick **Then** separate `[Benchmark]` methods time each of `TimeSystem`, `ResourceSystem`, `ControlSystem`, `ProvincePopulationGrowthSystem`, and `CountryScoreSystem` individually, calling each system's static `Update(...)` directly against the same shared populated-world fixture (same construction as `src/Game.Tests` unit tests use for these systems, minus assertions) — so a regression can be attributed to one system instead of only "the tick got slower."
- **Given** `OrgScoreSystem`'s scoring query is not ticked every frame inside `GameLogic.Update` but is called heavily by the eval harness (`Docs/Specs/26_07_16_14_bot-feature-eval-harness/`) once per org per sample **Then** it also gets its own `[Benchmark]` method against the populated fixture, since its cost directly bounds eval-batch wall-clock time.
- **Given** BenchmarkDotNet's built-in memory diagnoser **Then** every benchmark class has `[MemoryDiagnoser]` enabled so allocated-bytes-per-op is captured alongside execution time in every run and every snapshot — reported, per the gating rule below, but not itself gated on in v1.

### Snapshots — committed baselines and history

- **Given** a benchmark run completes **Then** results are exportable as JSON (BenchmarkDotNet's JSON exporter), and a `--compare` CLI mode (`dotnet run --project src/Game.Benchmarks -c Release -- --compare`) runs the full benchmark suite and diffs it against the committed baseline described below, without requiring a human to eyeball two separate BenchmarkDotNet console reports.
- **Given** the project's convention of committing derived artifacts under `Docs/` (mirroring `Docs/BotFeatures/<featureId>/eval_history.json` + `eval_summary.md`) **Then** results are persisted at `Docs/Benchmarks/baseline.json` (the current accepted baseline, one entry per benchmark name: mean, error, stddev, allocated bytes), `Docs/Benchmarks/history.json` (append-only: every `--compare` run's full result set plus its verdict, timestamped, never rewritten or deleted — same append-only rule as the eval harness's history file), and a human-readable `Docs/Benchmarks/summary.md` rewritten after each run to reflect the latest comparison (per-benchmark before/after, %, pass/fail).
- **Given** no baseline exists yet (first run ever, or a benchmark name that's new) **Then** `--compare` treats that benchmark as automatically passing (nothing to regress against) and reports it as "new — no baseline," rather than erroring or fabricating a comparison.
- **Given** an intentional, reviewed performance change (improvement or an accepted tradeoff) **Then** a separate `--update-baseline` CLI mode overwrites `Docs/Benchmarks/baseline.json` with the just-run results (after appending the run to `history.json` and rewriting `summary.md` same as `--compare`) — baseline updates are always an explicit, separate action, never an implicit side effect of running `--compare`.
- **Given** BenchmarkDotNet timings are machine- and environment-dependent **Then** this is documented as a known limitation directly in `Docs/Benchmarks/summary.md`'s header: baseline comparisons are only meaningful when `--compare` runs are produced on comparable hardware to the committed baseline (e.g. consistently within the same CI/dev-container class of machine as used originally) — no cross-machine normalization is attempted in v1.

### Regression gate

- **Given** a `--compare` run **Then** the pass/fail verdict per benchmark is `meanCurrent ≤ meanBaseline × (1 + epsilonRelative)`, with `epsilonRelative` defaulting to `0.05` (5%) — looser than the eval harness's `0.02` score-gate default, because wall-clock micro-benchmarks are inherently noisier than seeded simulation deltas. `epsilonRelative` is a CLI-overridable constant (not read from a per-feature config file — there is exactly one harness, not one config per benchmarked thing).
- **Given** the overall `--compare` exit code **Then** it is `0` iff **every** benchmark with an existing baseline passes the gate above; any single regression makes the process exit non-zero with the failing benchmark name(s) and their %-slower figures printed to stderr — mirroring `Game.Evals`' exit-code-is-the-verdict contract, so `--compare` can serve directly as a Ralph task `gate` command.
- **Given** allocated-bytes-per-op is captured (`[MemoryDiagnoser]`) **Then** it is reported in `summary.md`/`history.json` alongside timing for every benchmark but is **not** part of the v1 gate — an allocation regression is visible to a human reviewer but does not fail `--compare` on its own.

### Existing-tests safety net

- **Given** any performance change necessarily touches the same production code paths covered by `src/Game.Tests` **Then** both skills below always run `dotnet test src/GlobalStrategy.Core.sln` in addition to the benchmark gate — a "faster but wrong" change is never accepted merely because `--compare` passed.

### `/benchmark-report` skill (simple compare/report)

- **Given** day-to-day use with no autonomous loop **Then** a skill `.claude/commands/benchmark-report.md` exists, taking no required arguments, that: builds Release, runs `dotnet run --project src/Game.Benchmarks -c Release -- --compare`, and presents the resulting `summary.md` comparison table (per benchmark: baseline mean, current mean, % change, pass/fail, allocated bytes) directly in chat — it does not modify any source file, does not commit, and does not open a PR.
- **Given** the user explicitly wants to accept the current numbers as the new normal (e.g. after a reviewed, intentional change) **Then** the skill supports an explicit `$ARGUMENTS` value (e.g. `/benchmark-report update-baseline`) that runs `--update-baseline` instead of `--compare` and reports what changed in the baseline — this remains a human-invoked, explicit action, never automatic.

### `/optimize-performance` skill (autonomous Ralph loop)

- **Given** the repo's existing bot-feature Ralph-loop precedent **Then** a skill `.claude/commands/optimize-performance.md` exists, taking `$ARGUMENTS` = a target: either a specific benchmarked name (e.g. `ControlSystem`, `full-tick`) or `all`, and follows the same shape as `/implement-bot-feature`: requires a clean working tree, creates/switches to `ralph/perf_<target>`, and writes `.ralph/prd.md` **directly** (no `/specify`/`/plan` per-attempt — see Constitution carve-out below) resetting `.ralph/activity.md` to its header.
- **Given** an authority statement (mirroring `/implement-bot-feature`'s) **Then** the skill's markdown states explicitly: this is the sanctioned autonomous path for **micro-optimizations to already-implemented `src/` game-core logic only** — algorithmic/allocation changes inside existing systems' `Update` methods and the pure-C# helpers they call, gated by the benchmark harness plus the full test suite. Any change that alters observable gameplay behavior, public system signatures in a way other callers aren't updated for, ECS component/archetype shape in a way not justified purely by the perf change, or anything under `Assets/` is out of authority — the skill stops and directs the user to `/specify` + `/plan` instead of routing around the gate.
- **Given** the PRD task loop **Then** it writes one task (`category: "perf-optimization"`) whose `gate` is `dotnet test src/GlobalStrategy.Core.sln && dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark <target>` (both must pass; `--benchmark all` when the target is `all`) and whose encoded `steps` mirror the bot-feature loop: run the gate; if it passes, mark `passes: true` and stop; if it fails, read `Docs/Benchmarks/history.json`'s latest entry and `.ralph/activity.md`, pick **one** concrete optimization attempt, journal it, and re-run; if a bounded attempt budget (default `5`, independent of the Ralph driver's own `-MaxIterations`) is exhausted without passing, journal budget exhaustion and end with `passes: false` — no PR opened as "done."
- **Given** the loop passes **Then** the skill's finishing behavior (executed by the driver, same as `/implement-bot-feature`) runs `--update-baseline` to commit the new, faster (or at least non-regressed) numbers as the accepted baseline, commits via **/commit**, and hands off to `/complete-prd` for a human-reviewable PR whose body includes the before/after benchmark table for the target — the loop never merges; human review remains the merge gate, because a benchmark and a test suite passing cannot judge subtler tradeoffs (code readability, maintainability) that only a human reviewer weighs.
- **Given** the budget is exhausted without a passing attempt **Then** no baseline update happens, no PR is opened, and the branch/activity journal/history are left as-is for a human to pick up — identical failure semantics to `/implement-bot-feature`.

### Constitution carve-out

- **Given** the Constitution's Planning Discipline principle ("plan before implement") **Then** `Docs/Constitution.md` is amended with a second carve-out, alongside the existing bot-feature one: performance-optimization attempts made via `/optimize-performance` — changes strictly within the authority statement above — use the skill's directly-written PRD plus the committed `Docs/Benchmarks/history.json` as their planning artifact, under this spec (`Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`) as the standing spec/plan. Everything outside that narrow surface (including the harness project itself, and the two skills' own markdown files) still goes through the normal `/specify` + `/plan` flow — this spec is that flow for the harness's initial build.

## Out of Scope

- **CI integration.** Benchmarks are run locally by a developer or by the skills; wiring `--compare` into a CI pipeline as a merge gate is future work, same precedent as the eval harness.
- **Cross-machine baseline normalization.** No attempt to make `Docs/Benchmarks/baseline.json` portable across differently-specced machines; this is documented as a caveat, not solved.
- **Unity-side / Assets profiling.** No Unity Profiler integration, no WebGL build performance, no rendering/GPU benchmarks — this harness is `src/` C# game-core logic only, same boundary `Game.Evals`/`Game.ConsoleRunner` already draw.
- **Allocation-based gating.** `[MemoryDiagnoser]` output is captured and reported every run but does not fail `--compare` in v1.
- **Automatic revert-on-regression in production/runtime.** This is a dev-time benchmarking harness, not a runtime performance monitor.
- **A genetic/statistical optimizer for finding improvements.** `/optimize-performance`'s loop picks one human-legible change per attempt via the same journaled reasoning as `/implement-bot-feature`'s loop, not an automated search over code transformations.
- **Benchmarking config-loading/startup or save/load serialization.** Considered and explicitly deferred — v1 covers the per-tick systems plus `OrgScoreSystem` only; a follow-up spec can extend the harness to `InitSystem`/`WorldSnapshot` if startup or save/load time becomes a concern.

## Resolved Decisions

Resolved with the user during `/specify`:

- **What is benchmarked:** both the full `GameLogic.Update` tick and individually-benchmarked hot systems (not one or the other) — see "What is benchmarked" above.
- **Snapshot storage:** committed under `Docs/Benchmarks/` (baseline + append-only history + human-readable summary), not gitignored scratch — regressions must be visible in PR diffs.
- **Skill scope:** both a simple compare/report skill (`/benchmark-report`, human-driven, no autonomy) and a full Ralph-loop autonomous optimization skill (`/optimize-performance`, bounded attempts, gated, ends in PR) are in scope for this spec — not a choice between them.
