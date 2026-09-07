using System;
using System.Collections.Generic;
using System.IO;
using GS.Game.E2E;
using Xunit;

namespace GS.Game.Tests {
	public class E2EStepScriptTests {
		static string FindRepoRoot() {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Docs", "E2E", "flows"))) {
				dir = dir.Parent;
			}
			if (dir == null) {
				throw new InvalidOperationException("Could not locate repo root containing Docs/E2E/flows.");
			}
			return dir.FullName;
		}

		static StepScript SampleScript() {
			return new StepScript {
				Name = "sample",
				Steps = new List<StepDefinition> {
					new StepDefinition { Kind = StepKinds.Click, Name = "btn-play" },
					new StepDefinition { Kind = StepKinds.SelectOrg, Org = "{{org}}" },
					new StepDefinition { Kind = StepKinds.WaitFor, Screen = "Map" }
				}
			};
		}

		[Fact]
		public void valid_script_round_trips_through_serializer() {
			var original = SampleScript();

			string json = StepScriptSerializer.Serialize(original);
			var restored = StepScriptSerializer.Deserialize(json);

			Assert.Equal(original.Name, restored.Name);
			Assert.Equal(original.Steps.Count, restored.Steps.Count);
			Assert.Equal(original.Steps[0].Kind, restored.Steps[0].Kind);
			Assert.Equal(original.Steps[0].Name, restored.Steps[0].Name);
			Assert.Equal(original.Steps[1].Org, restored.Steps[1].Org);
			Assert.Equal("Map", restored.Steps[2].Screen);
			Assert.Contains("\"kind\": \"click\"", json);
		}

		[Fact]
		public void unknown_step_kind_fails_validation_naming_script_and_index() {
			var script = new StepScript {
				Name = "broken",
				Steps = new List<StepDefinition> {
					new StepDefinition { Kind = "teleport", Name = "anywhere" }
				}
			};

			var result = StepScriptValidator.Validate(script);

			Assert.False(result.Success);
			Assert.Contains("broken step 0", result.Error);
			Assert.Contains("teleport", result.Error);
		}

		[Fact]
		public void step_missing_required_target_fails_validation_naming_script_and_index() {
			var script = new StepScript {
				Name = "broken",
				Steps = new List<StepDefinition> {
					new StepDefinition { Kind = StepKinds.Click }
				}
			};

			var result = StepScriptValidator.Validate(script);

			Assert.False(result.Success);
			Assert.Contains("broken step 0", result.Error);
			Assert.Contains("name or label", result.Error);
		}

		[Fact]
		public void placeholders_substitute_from_run_request_inputs() {
			var script = SampleScript();
			var inputs = new Dictionary<string, string> { { "org", "illuminati" } };

			var result = ParameterSubstitution.Apply(script, inputs);

			Assert.True(result.Success);
			Assert.Equal("illuminati", result.Script!.Steps[1].Org);
			Assert.Equal("btn-play", result.Script.Steps[0].Name);
		}

		[Fact]
		public void unresolved_placeholder_is_a_validation_error() {
			var script = SampleScript();

			var result = ParameterSubstitution.Apply(script, new Dictionary<string, string>());

			Assert.False(result.Success);
			Assert.Contains("sample step 1", result.Error);
			Assert.Contains("{{org}}", result.Error);
			Assert.DoesNotContain("{{org}}", result.Script?.Steps[1].Org ?? "");
		}

		[Fact]
		public void committed_new_game_to_map_flow_parses_and_validates() {
			AssertFlowParsesAndValidates("new_game_to_map.json");
		}

		[Fact]
		public void committed_load_save_to_map_flow_parses_and_validates() {
			AssertFlowParsesAndValidates("load_save_to_map.json");
		}

		static void AssertFlowParsesAndValidates(string fileName) {
			string path = Path.Combine(FindRepoRoot(), "Docs", "E2E", "flows", fileName);
			string json = File.ReadAllText(path);
			var script = StepScriptSerializer.Deserialize(json);
			var result = StepScriptValidator.Validate(script);
			Assert.True(result.Success, result.Error);
			Assert.Equal(Path.GetFileNameWithoutExtension(fileName), script.Name);
			Assert.NotEmpty(script.Steps);
		}
	}
}
