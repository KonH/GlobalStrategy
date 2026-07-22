Autonomously attempt a bounded, gated micro-optimization of an already-implemented `src/` game-core hot path, driven by the BenchmarkDotNet harness (`Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`), and scaffold a Ralph loop run that ends in a human-reviewable PR.

Arguments: `$ARGUMENTS` — a target: a benchmark class-name substring (e.g. `CountryPopulationCollector`, `FullTick`, `ResourceSystem`) or `all`.

## Authority statement

This skill is the sanctioned autonomous path for **micro-optimizations to already-implemented `src/` game-core logic only**: algorithmic/allocation changes inside an existing system's/collector's `Update`/`Compute`/query method and the pure-C# helpers it calls, gated by `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`'s harness plus the full test suite. `Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/` (spec + plan) is the standing spec/plan for this surface, and the directly-written PRD plus the committed `Docs/Benchmarks/history.json` is the planning artifact — per the Constitution's Planning Discipline performance-optimization carve-out.

**Anything outside that narrow surface is out of authority** — stop and direct the user to `/specify` + `/plan` instead of routing around the gate:
- Changes that alter observable gameplay behavior.
- A public signature change other callers aren't updated for.
- Any ECS component/archetype shape change not strictly required by the perf change.
- Anything under `Assets/`.
- The harness project itself (`src/Game.Benchmarks`) or either skill's own markdown.

## Steps

1. **Resolve the target's benchmark class(es).** Confirm `$ARGUMENTS` (or `all`) matches at least one class under `src/Game.Benchmarks` (e.g. via `Grep` for `class.*Benchmarks` filtered by the substring). If nothing matches, stop and report — do not guess a class name.

2. **Branch.** Require a clean working tree (`git status`, never `-uall`). Create or switch to `ralph/perf_<target>` (the exact branch name `scripts/automation/claude/ralph.ps1 -PerfTarget <target>` resolves to).

3. **Write `.ralph/prd.md` directly** (no `/specify`/`/plan`/`/create-prd`), reset `.ralph/activity.md` to its header only (`# Ralph Activity Journal` + intro line + `---`). One task, `category: "perf-optimization"`:
   - `gate`: `dotnet test src/GlobalStrategy.Core.sln && dotnet run --project src/Game.Benchmarks -c Release -- --compare --benchmark <target>` (`--benchmark all` when target is `all`) — both must pass; never a fabricated always-green gate.
   - `steps` (written literally into the task, filling in `<target>` and `baseAttempt` = the current attempt count already recorded in `Docs/Benchmarks/history.json` for this target's benchmark name(s) before this run — `0` if none yet):
     1. Run the gate.
     2. Pass → set this task's `passes: true`, stop iterating.
     3. Fail → read `Docs/Benchmarks/history.json`'s latest relevant entries + `.ralph/activity.md`. Pick **one** concrete optimization attempt to the target's `src/` code (an algorithmic change, an allocation reduction, a caching/lookup-structure change — never a change to observable behavior). Journal the change and why in `.ralph/activity.md`, then re-run the gate.
     4. If attempts since `baseAttempt` reach `5` and the gate still fails: journal budget exhaustion, leave `passes: false`, stop — independent of the Ralph driver's own `-MaxIterations`.

4. **Commit the scaffolding** (branch + PRD + reset activity) via the **/commit** skill's rules, matching `/implement-bot-feature`'s step 5.

5. **Hand off** — print exactly this for the user to run in a terminal, this skill does **not** spawn the loop itself:
   ```
   .\scripts\ralph.ps1 -PerfTarget <target>
   ```

## Finish / failure semantics

Executed by the driver's phases (`scripts/automation/claude/ralph.ps1 -PerfTarget <target>` → `scripts/automation/claude/ralph.py`), not by this skill directly:

- **All gates pass:** the driver runs `--update-baseline` for the target (`dotnet run --project src/Game.Benchmarks -c Release -- --update-baseline --benchmark <target>`), commits via **/commit**, then hands off to `/complete-prd perf:<target>` — PR body carries the before/after benchmark table for the target, sourced from `Docs/Benchmarks/history.json`/`summary.md`. The loop **never merges** — human review gates merge, because a benchmark and a test suite passing cannot judge subtler tradeoffs (readability, maintainability) that only a human reviewer weighs.
- **Budget exhausted / loop incomplete:** no baseline update, no PR opened as "done." The committed-artifact state reached on the branch stays as-is; the failure report is `Docs/Benchmarks/history.json` + `Docs/Benchmarks/summary.md` (which attempts failed the gate and by how much) and `.ralph/activity.md`. Nothing is force-reverted. A human can run `claude -p "/complete-prd perf:<target>"` manually if they want a PR anyway.
