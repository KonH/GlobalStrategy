---
name: dotnet-benchmark
description: Run the Game.Benchmarks harness (--compare/--update-baseline) from the project root without `cd`, logging output to a single file and checking exit code + a targeted grep instead of reading the full (very verbose) BenchmarkDotNet log.
---

# dotnet benchmark

Runs `dotnet run --project src/Game.Benchmarks -c Release --` against the BenchmarkDotNet
perf harness (`Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`), avoiding the same
habits `dotnet-build`/`dotnet-test` avoid, plus one more specific to this harness:

1. **Never prefix the command with `cd`.** The Bash tool's working directory is already
   the project root for every call — a leading `cd` is redundant and (per this project's
   shell rule) triggers an extra permission prompt for no benefit.
2. **Log to one file, don't re-run to see more.** Redirect stdout+stderr to a single log
   file.
3. **Don't `Read` the whole log.** BenchmarkDotNet's own console output is extremely
   verbose (per-iteration pilot/warmup/actual lines, one block per benchmark method) —
   reading it in full wastes context for no benefit. Check the **exit code** first (it is
   the verdict — see `Docs/Benchmarks/README` conventions), then use a single targeted
   Bash `grep`/`tail` to pull just the parts that matter: the `| Method | Mean | ... |`
   summary table(s), and anything after `Regression(s) detected:` / `Benchmark(s) produced
   no results` / `No benchmarks matched.` on stderr.

## Args

`/dotnet-benchmark (--compare|--update-baseline) [--benchmark <name>] [--epsilon <value>]`

- Exactly one of `--compare`/`--update-baseline` is required (mirrors the harness's own
  CLI contract — see `src/Game.Benchmarks/Program.cs`).
- **--benchmark** (optional): case-insensitive substring filter against a benchmark class
  name (e.g. `ControlSystem`, `CountryPopulationCollector`). Omit to run the full suite —
  full-suite runs are slow (each benchmark method costs roughly 20-40s under
  BenchmarkDotNet's default job), so prefer `run_in_background: true` for the Bash call,
  or scope to `--benchmark <name>` while iterating.
- **--epsilon** (optional, `--compare` only): overrides the default 5% regression gate.

## Steps

1. Pick the log path: `.tmp/dotnet-benchmark.log` (gitignored, per
   `.claude/rules/temp_scripts.md`).
2. Run, in a single Bash tool call, with `dangerouslyDisableSandbox: true` (per this
   project's dotnet-command convention):

   ```
   dotnet run --project src/Game.Benchmarks -c Release -- <args> > .tmp/dotnet-benchmark.log 2>&1
   echo "EXIT:$?"
   ```

   For a full unfiltered suite run, pass `run_in_background: true` on the Bash call
   instead of waiting inline (a full pass across every benchmark class can run well past a
   single 10-minute foreground Bash timeout).
3. Check the exit code printed by the `echo` line — `0` for `--compare` means every
   gated benchmark passed (or is new, with no baseline yet); non-zero means a regression,
   a benchmark that produced no results, or a `--benchmark` filter that matched nothing.
   `--update-baseline` returns non-zero only on an execution error.
4. Pull just the useful parts with a single grep/tail Bash call (do not `Read` the full
   log — it is dominated by per-iteration BenchmarkDotNet diagnostic lines):
   ```
   grep -A5 "^| Method" .tmp/dotnet-benchmark.log
   tail -20 .tmp/dotnet-benchmark.log
   ```
   Combine both greps in one Bash call when possible.
5. Cross-check `Docs/Benchmarks/summary.md` (rewritten by every run) if a persisted,
   pre-formatted comparison table is more useful than parsing console output.
6. Delete the log when done with it, as a separate Bash call, per the temp-scripts
   run-then-delete convention.

## Notes

- The harness's own CLI parses its flags itself and never forwards the raw process `args`
  into BenchmarkDotNet's own parser — see the comment in `Program.cs`.
- A Debug build is rejected with a clear stderr message and exit code `2` before touching
  BenchmarkDotNet at all — always pass `-c Release`.
