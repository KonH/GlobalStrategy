using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace GS.Game.Benchmarks {
	static class Program {
		static int Main(string[] args) {
#if DEBUG
			Console.Error.WriteLine(
				"Game.Benchmarks must be run with -c Release (BenchmarkDotNet requires a Release build for valid measurements).");
			return 2;
#else
			CliOptions options;
			try {
				options = CliOptions.Parse(args);
			} catch (ArgumentException ex) {
				Console.Error.WriteLine($"Usage error: {ex.Message}");
				Console.Error.WriteLine(
					"Usage: dotnet run --project src/Game.Benchmarks -c Release -- (--compare|--update-baseline) [--benchmark <name>] [--epsilon <value>]");
				return 2;
			}

			// GameWorldFixture resolves "Assets/Configs" relative to the repo root. BenchmarkDotNet's
			// (default, and preferred for isolation) out-of-process toolchain builds and runs each
			// benchmark class from a separate copy under bin/.../Game.Benchmarks-<Job>/, whose
			// working directory is that subfolder, not the repo root. Rather than giving up process
			// isolation (InProcessEmitToolchain), pass the real repo root down via an environment
			// variable, which BenchmarkDotNet's spawned subprocess inherits - GameWorldFixture reads
			// it back (see GameWorldFixture.RepoRoot).
			Environment.SetEnvironmentVariable(GameWorldFixture.RepoRootEnvVar, Directory.GetCurrentDirectory());

			// The harness's own CLI args are parsed above and never forwarded to
			// BenchmarkDotNet's own parser - only strings this harness constructs itself
			// (a --filter built from --benchmark) are ever passed to BenchmarkSwitcher.Run.
			var config = ManualConfig.Create(DefaultConfig.Instance).AddDiagnoser(MemoryDiagnoser.Default);
			// BenchmarkSwitcher.Run treats zero args as "prompt interactively for a selection",
			// not "run everything" - an explicit `--filter *` is required to run the full suite
			// non-interactively.
			string[] runnerArgs = options.BenchmarkFilter == null
				? new[] { "--filter", "*" }
				: new[] { "--filter", $"*{options.BenchmarkFilter}*" };

			Summary[] summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(runnerArgs, config).ToArray();
			bool anyRequested = summaries.Length > 0 && summaries.Any(s => s.BenchmarksCases.Length > 0);
			if (!anyRequested) {
				Console.Error.WriteLine("No benchmarks matched.");
				return 1;
			}

			// A benchmark that BenchmarkDotNet itself failed to execute (crashed, or otherwise
			// produced no workload results) must never be silently recorded as a fabricated
			// "0ns, pass" entry - that would defeat the entire point of this harness. Fail loudly
			// instead of writing a misleading snapshot.
			var brokenNames = new List<string>();
			var results = Flatten(summaries, brokenNames);
			if (brokenNames.Count > 0) {
				Console.Error.WriteLine("Benchmark(s) produced no results (see BenchmarkDotNet output above for the cause):");
				foreach (var name in brokenNames) { Console.Error.WriteLine($"  {name}"); }
				return 1;
			}

			var baselineBeforeUpdate = SnapshotStore.LoadBaseline();
			var record = new BenchmarkRunRecord {
				Timestamp = DateTime.UtcNow,
				Mode = options.UpdateBaseline ? "update-baseline" : "compare",
				Results = results
			};

			bool overallPass = true;
			var failures = new List<string>();
			foreach (var entry in results) {
				double? baselineMean = baselineBeforeUpdate.TryGetValue(entry.Name, out var baselineEntry)
					? baselineEntry.MeanNanoseconds
					: (double?)null;
				bool pass = BenchmarkGate.Passes(entry.MeanNanoseconds, baselineMean, options.Epsilon);
				record.Verdicts[entry.Name] = pass;
				if (!pass) {
					overallPass = false;
					double percentSlower = baselineMean.HasValue && baselineMean.Value > 0
						? (entry.MeanNanoseconds - baselineMean.Value) / baselineMean.Value * 100.0
						: 0.0;
					failures.Add($"{entry.Name}: {percentSlower:F1}% slower than baseline");
				}
			}
			record.Pass = overallPass;

			SnapshotStore.AppendHistory(record);
			SnapshotStore.RewriteSummary(record, baselineBeforeUpdate);

			if (options.UpdateBaseline) {
				SnapshotStore.SaveBaseline(results);
				Console.WriteLine($"Baseline updated: {results.Count} benchmark(s).");
				return 0;
			}

			if (!overallPass) {
				Console.Error.WriteLine("Regression(s) detected:");
				foreach (var failure in failures) { Console.Error.WriteLine($"  {failure}"); }
				return 1;
			}

			Console.WriteLine($"Compare passed: {results.Count} benchmark(s).");
			return 0;
#endif
		}

		static List<BenchmarkEntry> Flatten(Summary[] summaries, List<string> brokenNames) {
			var entries = new List<BenchmarkEntry>();
			foreach (var summary in summaries) {
				foreach (var report in summary.Reports) {
					var descriptor = report.BenchmarkCase.Descriptor;
					string paramsInfo = report.BenchmarkCase.Parameters.Count > 0
						? $"({report.BenchmarkCase.Parameters.PrintInfo})"
						: "";
					string name = $"{descriptor.Type.Name}.{descriptor.WorkloadMethod.Name}{paramsInfo}";
					if (report.ResultStatistics == null) {
						brokenNames.Add(name);
						continue;
					}
					entries.Add(new BenchmarkEntry {
						Name = name,
						MeanNanoseconds = report.ResultStatistics.Mean,
						StdDevNanoseconds = report.ResultStatistics.StandardDeviation,
						AllocatedBytes = report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase) ?? 0
					});
				}
			}
			return entries;
		}
	}

	class CliOptions {
		public bool UpdateBaseline;
		public string? BenchmarkFilter;
		public double Epsilon = BenchmarkGate.DefaultEpsilonRelative;

		public static CliOptions Parse(string[] args) {
			var options = new CliOptions();
			bool? compareMode = null;
			for (int i = 0; i < args.Length; i++) {
				switch (args[i]) {
					case "--compare":
						compareMode = true;
						break;
					case "--update-baseline":
						compareMode = false;
						break;
					case "--benchmark":
						options.BenchmarkFilter = NextArg(args, ref i, "--benchmark");
						break;
					case "--epsilon":
						options.Epsilon = double.Parse(NextArg(args, ref i, "--epsilon"));
						break;
					default:
						throw new ArgumentException($"Unknown argument: {args[i]}");
				}
			}
			if (compareMode == null) {
				throw new ArgumentException("Exactly one of --compare or --update-baseline is required.");
			}
			options.UpdateBaseline = compareMode == false;
			if (options.BenchmarkFilter != null && options.BenchmarkFilter.Equals("all", StringComparison.OrdinalIgnoreCase)) {
				options.BenchmarkFilter = null;
			}
			return options;
		}

		static string NextArg(string[] args, ref int i, string flag) {
			if (i + 1 >= args.Length) { throw new ArgumentException($"{flag} requires a value."); }
			i++;
			return args[i];
		}
	}
}
