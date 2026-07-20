Run the BenchmarkDotNet performance harness (`Docs/Specs/26_07_18_18_benchmarkdotnet-perf-harness/`) and report the comparison table in chat. No source edits, no commit, no PR — purely informational.

Arguments: `$ARGUMENTS` — optional, one recognized value: `update-baseline`.

## Steps

1. Build Release: use the **dotnet-build** skill with `src/GlobalStrategy.Core.sln` and `Release`.
2. Run the harness:
   - If `$ARGUMENTS == "update-baseline"`: use the **dotnet-benchmark** skill with `--update-baseline`.
   - Otherwise: use the **dotnet-benchmark** skill with `--compare`.

   This is a full-suite run — expect it to take several minutes (each benchmark class does
   its own out-of-process build+run). Prefer `run_in_background: true` on the underlying
   Bash call.
3. Read `Docs/Benchmarks/summary.md` (just rewritten by the run) with the `Read` tool and
   present its table directly in chat, verbatim — never fabricate or approximate numbers.
4. If the run's exit code was non-zero (a regression, under `--compare`), call that out
   explicitly before showing the table, quoting the failing benchmark names from stderr.
5. Do not edit any source file, stage anything, commit, or open a PR — this skill is
   read-only reporting. The human decides what (if anything) to do next, e.g. running
   `/optimize-performance <target>` on a flagged regression.
