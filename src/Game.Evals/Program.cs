using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GS.Configs.IO;
using GS.Game.Bots;
using GS.Game.Configs;
using GS.Game.ConsoleRunner;

namespace GS.Game.Evals {
	public static class Program {
		const string ConfigDir = "Assets/Configs";

		public static int Main(string[] args) {
			string? featureId = null;
			string? evalConfigPath = null;
			string? outDir = null;

			for (int i = 0; i < args.Length; i++) {
				switch (args[i]) {
					case "--feature":
						featureId = NextArg(args, ref i, "--feature");
						break;
					case "--eval-config":
						evalConfigPath = NextArg(args, ref i, "--eval-config");
						break;
					case "--out-dir":
						outDir = NextArg(args, ref i, "--out-dir");
						break;
					default:
						Console.Error.WriteLine($"Unknown argument '{args[i]}'.");
						return 2;
				}
			}

			if (string.IsNullOrEmpty(featureId)) {
				Console.Error.WriteLine("--feature is required.");
				return 2;
			}

			var registry = BotFeatureRegistry.CreateDefault();
			if (!registry.IsRegistered(featureId)) {
				Console.Error.WriteLine($"Unknown bot feature id '{featureId}'.");
				return 2;
			}

			EvalConfig config;
			if (evalConfigPath != null) {
				if (!File.Exists(evalConfigPath)) {
					Console.Error.WriteLine($"Eval config not found: '{evalConfigPath}'.");
					return 2;
				}
				config = EvalConfig.Load(evalConfigPath);
			} else {
				string defaultPath = Path.Combine("Docs", "BotFeatures", featureId, "eval_config.json");
				if (File.Exists(defaultPath)) {
					config = EvalConfig.Load(defaultPath);
				} else {
					Console.Error.WriteLine($"Note: no eval config found at '{defaultPath}', using defaults.");
					config = EvalConfig.Default();
				}
			}

			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(ConfigDir, "organizations.json")).Load();
			if (orgConfig.Organizations.Count == 0) {
				Console.Error.WriteLine("organizations.json declares no organizations.");
				return 2;
			}
			string candidateOrgId = config.CandidateOrgId ?? orgConfig.Organizations[0].OrganizationId;
			if (orgConfig.FindById(candidateOrgId) == null) {
				Console.Error.WriteLine($"Candidate org '{candidateOrgId}' is not in organizations.json.");
				return 2;
			}

			var candidateFeatures = config.ResolveCandidateFeatures(featureId);
			var candidateEntry = candidateFeatures.FirstOrDefault(f => f.FeatureId == featureId);
			if (candidateEntry == null || !candidateEntry.Enabled) {
				Console.Error.WriteLine($"candidateFeatures must declare '{featureId}' enabled — otherwise the candidate arm is indistinguishable from the baseline arm.");
				return 2;
			}
			var baselineFeatures = EvalConfig.BuildBaselineFeatures(candidateFeatures, featureId);

			var seeds = SeedDerivation.Seeds(config.BaseSeed, config.SeedCount);
			var parameterSets = ParameterSearch.Generate(config.ParameterSearch);

			int totalRuns = seeds.Count * (1 + parameterSets.Count);
			if (totalRuns > config.MaxTotalRuns) {
				Console.Error.WriteLine($"Run cap exceeded: {totalRuns} runs > maxTotalRuns={config.MaxTotalRuns}.");
				return 2;
			}

			int attempt = EvalPersistence.NextAttemptNumber(featureId);
			string attemptDir = outDir ?? Path.Combine(".tmp", "evals", featureId, $"attempt_{attempt}");
			Directory.CreateDirectory(attemptDir);
			string profilesDir = Path.Combine(attemptDir, "profiles");
			Directory.CreateDirectory(profilesDir);

			var opponentOrgIds = orgConfig.Organizations
				.Select(o => o.OrganizationId)
				.Where(id => id != candidateOrgId)
				.ToList();

			string endDate = config.EndDate ?? DefaultEndDate();

			BatchRunner.RunOutcome RunOne(BatchRunner.RunRequest request) {
				var candidateFeatureList = request.Arm == "baseline" ? baselineFeatures : candidateFeatures;
				var effectiveCandidateFeatures = ApplyParameters(candidateFeatureList, featureId, request.Parameters);

				string suffix = request.Arm == "baseline"
					? $"baseline_seed{request.Seed}"
					: parameterSets.Count > 1
						? $"candidate_p{request.ParameterSetIndex}_seed{request.Seed}"
						: $"candidate_seed{request.Seed}";
				string outputPath = Path.Combine(attemptDir, $"{suffix}.json");

				string candidateProfilePath = Path.Combine(profilesDir, $"{suffix}_candidate.json");
				WriteProfile(candidateProfilePath, candidateOrgId, effectiveCandidateFeatures);

				var botPaths = new List<string> { candidateProfilePath };
				foreach (var oppOrgId in opponentOrgIds) {
					string oppPath = Path.Combine(profilesDir, $"{suffix}_{oppOrgId}.json");
					WriteProfile(oppPath, oppOrgId, config.OpponentFeatures);
					botPaths.Add(oppPath);
				}

				var runnerArgs = new List<string> {
					"--headless", "--seed", request.Seed.ToString(),
					"--output", outputPath,
					"--config-dir", ConfigDir,
					"--end-date", endDate,
					"--hours-per-tick", config.HoursPerTick.ToString(),
					"--timeout-seconds", config.TimeoutSeconds.ToString()
				};
				foreach (var path in botPaths) {
					runnerArgs.Add("--bot");
					runnerArgs.Add(path);
				}

				try {
					var options = HeadlessOptions.Parse(runnerArgs.ToArray());
					int exitCode = HeadlessRunner.Run(options);
					if (exitCode != 0) {
						return new BatchRunner.RunOutcome { Success = false, Error = $"headless runner exited {exitCode}" };
					}
				} catch (Exception ex) {
					return new BatchRunner.RunOutcome { Success = false, Error = ex.Message };
				}

				SimulationResult result;
				try {
					string json = File.ReadAllText(outputPath);
					result = JsonSerializer.Deserialize<SimulationResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
						?? throw new InvalidOperationException("malformed results JSON");
				} catch (Exception ex) {
					return new BatchRunner.RunOutcome { Success = false, Error = $"malformed results: {ex.Message}" };
				}

				if (result.EndReason == "timeout") {
					return new BatchRunner.RunOutcome { Success = false, Error = "run ended with endReason=timeout" };
				}

				var orgResult = result.Orgs.FirstOrDefault(o => o.OrgId == candidateOrgId);
				if (orgResult == null) {
					return new BatchRunner.RunOutcome { Success = false, Error = $"malformed results: no entry for candidate org '{candidateOrgId}'" };
				}
				double score = orgResult.Score;

				var emissions = new List<(string FeatureId, string ActionId)>();
				var log = result.BotEmissions.FirstOrDefault(e => e.OrgId == candidateOrgId);
				if (log != null) {
					foreach (var emission in log.Emissions) {
						emissions.Add((emission.FeatureId, emission.ActionId));
					}
				}

				return new BatchRunner.RunOutcome { Success = true, Score = score, Emissions = emissions };
			}

			BatchRunner.BatchResult batchResult;
			try {
				batchResult = BatchRunner.Run(seeds, parameterSets, RunOne);
			} catch (BatchRunner.BatchFailure ex) {
				Console.Error.WriteLine($"Run failure: {ex.Message}");
				return 3;
			}

			var baselineScores = batchResult.BaselineRuns.Select(r => r.Score).ToList();
			bool commandOffPass = EmissionAssertions.BaselineArmClean(batchResult.BaselineRuns, featureId);
			double epsilon = GateEvaluator.ComputeEpsilon(baselineScores, config.EpsilonRelative, config.EpsilonAbsolute);

			var parameterSetRecords = new List<ParameterSetRecord>();
			int? winnerIndex = null;
			double winnerMean = double.NegativeInfinity;
			bool anySetPasses = false;
			bool anySetCommandOnPasses = false;

			foreach (var setOutcome in batchResult.ParameterSets) {
				var candidateScores = setOutcome.CandidateRuns.Select(r => r.Score).ToList();
				var stats = GateEvaluator.ComputeStatistics(baselineScores, candidateScores);
				bool scoreGatePass = GateEvaluator.ScoreGatePasses(stats.Mean, epsilon);
				bool commandOnPass = EmissionAssertions.CandidateArmActed(setOutcome.CandidateRuns, featureId, config.TargetActions);
				bool setPasses = scoreGatePass && commandOnPass && commandOffPass;

				if (commandOnPass) { anySetCommandOnPasses = true; }
				if (setPasses) {
					anySetPasses = true;
					if (stats.Mean > winnerMean) {
						winnerMean = stats.Mean;
						winnerIndex = setOutcome.Set.Index;
					}
				}

				var perSeed = new List<PerSeedRecord>();
				for (int i = 0; i < seeds.Count; i++) {
					perSeed.Add(new PerSeedRecord {
						Seed = seeds[i],
						BaselineScore = baselineScores[i],
						CandidateScore = candidateScores[i],
						Delta = stats.Deltas[i]
					});
				}

				parameterSetRecords.Add(new ParameterSetRecord {
					Index = setOutcome.Set.Index,
					Parameters = new Dictionary<string, double>(setOutcome.Set.Parameters),
					ScoreGatePass = scoreGatePass,
					CommandOnPass = commandOnPass,
					Stats = new StatsRecord {
						Mean = stats.Mean, Median = stats.Median, Min = stats.Min, Max = stats.Max, StdDev = stats.StdDev,
						Improved = stats.Improved, Worsened = stats.Worsened, Unchanged = stats.Unchanged
					},
					PerSeed = perSeed
				});
			}

			bool batchPass = anySetPasses;
			bool improved = winnerIndex.HasValue && winnerMean > 0;

			var record = new AttemptRecord {
				Attempt = attempt,
				Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
				Verdict = new VerdictRecord {
					Pass = batchPass,
					ScoreGate = parameterSetRecords.Any(p => p.ScoreGatePass),
					CommandOn = anySetCommandOnPasses,
					CommandOff = commandOffPass
				},
				Improved = improved,
				Epsilon = epsilon,
				EffectiveConfig = config,
				ParameterSets = parameterSetRecords,
				Winner = winnerIndex,
				RawRunDir = attemptDir
			};

			EvalPersistence.AppendHistory(featureId, record);
			EvalPersistence.WriteSummary(featureId, record, attempt);

			if (!batchPass) {
				Console.Error.WriteLine($"Eval batch failed for feature '{featureId}': scoreGate={record.Verdict.ScoreGate} commandOn={record.Verdict.CommandOn} commandOff={commandOffPass}");
				return 1;
			}

			Console.WriteLine($"Eval batch passed for feature '{featureId}': winner={winnerIndex} meanDelta={winnerMean:F3} improved={improved}");
			return 0;
		}

		static List<BotFeatureConfigEntry> ApplyParameters(List<BotFeatureConfigEntry> features, string featureId, IReadOnlyDictionary<string, double> parameters) {
			var result = new List<BotFeatureConfigEntry>();
			foreach (var f in features) {
				var clone = f.Clone();
				if (clone.FeatureId == featureId && parameters.Count > 0) {
					foreach (var kv in parameters) { clone.Parameters[kv.Key] = kv.Value; }
				}
				result.Add(clone);
			}
			return result;
		}

		static void WriteProfile(string path, string orgId, List<BotFeatureConfigEntry> features) {
			var profile = new BotProfile {
				OrgId = orgId,
				Features = features.Select(f => new BotFeatureSetting {
					FeatureId = f.FeatureId,
					Enabled = f.Enabled,
					Parameters = new Dictionary<string, double>(f.Parameters)
				}).ToList()
			};
			var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
			File.WriteAllText(path, JsonSerializer.Serialize(profile, options));
		}

		static string DefaultEndDate() {
			var settings = new FileConfig<GameSettings>(Path.Combine(ConfigDir, "game_settings.json")).Load();
			return $"{settings.StartYear + 5}-01-01";
		}

		static string NextArg(string[] args, ref int i, string flag) {
			if (i + 1 >= args.Length) { throw new ArgumentException($"{flag} requires a value."); }
			i++;
			return args[i];
		}
	}
}
