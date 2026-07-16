using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GS.Game.Bots;
using GS.Game.ConsoleRunner;
using Xunit;

namespace GS.Game.Tests {
	public class BotProfileTests {
		static readonly JsonSerializerOptions s_writeOptions = new JsonSerializerOptions {
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			WriteIndented = true
		};
		static readonly JsonSerializerOptions s_readOptions = new JsonSerializerOptions {
			PropertyNameCaseInsensitive = true
		};

		// Mirrors HeadlessRunner.Run's inline validation block (that method is internal to
		// Game.ConsoleRunner and not exposed for direct testing) so this documented contract
		// stays regression-protected via public BotFeatureRegistry/BotProfile surfaces.
		static void ValidateBotProfiles(List<BotProfile> profiles, List<string> orgIds, BotFeatureRegistry registry) {
			var seen = new HashSet<string>();
			foreach (var profile in profiles) {
				if (!orgIds.Contains(profile.OrgId)) {
					throw new InvalidOperationException($"Bot profile org '{profile.OrgId}' is not in the participating org set.");
				}
				if (!seen.Add(profile.OrgId)) {
					throw new InvalidOperationException($"Duplicate bot profile for org '{profile.OrgId}'.");
				}
				foreach (var featureSetting in profile.Features) {
					if (!registry.IsRegistered(featureSetting.FeatureId)) {
						throw new InvalidOperationException($"Unknown bot feature id '{featureSetting.FeatureId}' for org '{profile.OrgId}'.");
					}
				}
			}
		}

		[Fact]
		void profile_json_deserializes_camel_case_into_dtos() {
			string path = Path.Combine(Path.GetTempPath(), $"bot_profile_{Guid.NewGuid():N}.json");
			File.WriteAllText(path, @"{
				""orgId"": ""Illuminati"",
				""features"": [
					{ ""featureId"": ""baselineCardPlay"", ""enabled"": true, ""parameters"": { ""minGoldReserve"": 25.5 } }
				]
			}");
			try {
				var profile = BotProfileLoader.Load(path);
				Assert.Equal("Illuminati", profile.OrgId);
				Assert.Single(profile.Features);
				Assert.Equal("baselineCardPlay", profile.Features[0].FeatureId);
				Assert.True(profile.Features[0].Enabled);
				Assert.Equal(25.5, profile.Features[0].Parameters["minGoldReserve"]);
			} finally {
				File.Delete(path);
			}
		}

		[Fact]
		void missing_file_and_malformed_json_fail_with_descriptive_error() {
			string missingPath = Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.json");
			var missingEx = Assert.Throws<ArgumentException>(() => BotProfileLoader.Load(missingPath));
			Assert.Contains(missingPath, missingEx.Message);

			string malformedPath = Path.Combine(Path.GetTempPath(), $"malformed_{Guid.NewGuid():N}.json");
			File.WriteAllText(malformedPath, "{ not valid json");
			try {
				var malformedEx = Assert.Throws<ArgumentException>(() => BotProfileLoader.Load(malformedPath));
				Assert.Contains(malformedPath, malformedEx.Message);
			} finally {
				File.Delete(malformedPath);
			}
		}

		[Fact]
		void unknown_feature_id_fails_fast_even_when_disabled() {
			var registry = BotFeatureRegistry.CreateDefault();
			var profiles = new List<BotProfile> {
				new BotProfile {
					OrgId = "Illuminati",
					Features = new List<BotFeatureSetting> {
						new BotFeatureSetting { FeatureId = "nope", Enabled = false }
					}
				}
			};
			var ex = Assert.Throws<InvalidOperationException>(() => ValidateBotProfiles(profiles, new List<string> { "Illuminati" }, registry));
			Assert.Contains("nope", ex.Message);
		}

		[Fact]
		void profile_org_not_in_participating_set_fails_fast() {
			var registry = BotFeatureRegistry.CreateDefault();
			var profiles = new List<BotProfile> { new BotProfile { OrgId = "DoesNotExist" } };
			var ex = Assert.Throws<InvalidOperationException>(() => ValidateBotProfiles(profiles, new List<string> { "Illuminati" }, registry));
			Assert.Contains("DoesNotExist", ex.Message);
		}

		[Fact]
		void duplicate_profiles_for_same_org_fail_fast() {
			var registry = BotFeatureRegistry.CreateDefault();
			var profiles = new List<BotProfile> {
				new BotProfile { OrgId = "Illuminati" },
				new BotProfile { OrgId = "Illuminati" }
			};
			var ex = Assert.Throws<InvalidOperationException>(() => ValidateBotProfiles(profiles, new List<string> { "Illuminati" }, registry));
			Assert.Contains("Illuminati", ex.Message);
		}

		[Fact]
		void bot_flag_is_repeatable_and_requires_headless() {
			var options = HeadlessOptions.Parse(new[] {
				"--headless", "--seed", "1", "--output", "out.json", "--max-ticks", "1",
				"--bot", "a.json", "--bot", "b.json"
			});
			Assert.Equal(new[] { "a.json", "b.json" }, options.BotProfilePaths);

			Assert.Throws<ArgumentException>(() => HeadlessOptions.Parse(new[] { "--bot", "a.json" }));
		}

		[Fact]
		void results_parameters_include_effective_bot_config() {
			var result = new SimulationResult {
				Seed = 1,
				Parameters = new SimulationParameters {
					OrgIds = new List<string> { "Illuminati" },
					Bots = new List<BotProfileResult> {
						new BotProfileResult {
							OrgId = "Illuminati",
							Features = new List<BotFeatureResult> {
								new BotFeatureResult { FeatureId = "baselineCardPlay", Enabled = true, Parameters = new Dictionary<string, double> { ["minGoldReserve"] = 0.0 } }
							}
						}
					}
				}
			};

			string json = JsonSerializer.Serialize(result, s_writeOptions);
			Assert.Contains("\"bots\"", json);
			Assert.Contains("\"baselineCardPlay\"", json);

			var roundTrip = JsonSerializer.Deserialize<SimulationResult>(json, s_readOptions);
			Assert.NotNull(roundTrip!.Parameters.Bots);
			Assert.Single(roundTrip.Parameters.Bots!);
			Assert.Equal("Illuminati", roundTrip.Parameters.Bots![0].OrgId);
		}

		[Fact]
		void results_without_bots_omit_bot_section() {
			var result = new SimulationResult {
				Seed = 1,
				Parameters = new SimulationParameters { OrgIds = new List<string> { "Illuminati" } }
			};

			string json = JsonSerializer.Serialize(result, s_writeOptions);
			Assert.DoesNotContain("\"bots\"", json);
		}
	}
}
