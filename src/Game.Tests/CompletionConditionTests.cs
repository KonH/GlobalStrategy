using System;
using System.Collections.Generic;
using System.IO;
using ECS;
using GS.Configs.IO;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Newtonsoft.Json;
using Xunit;

namespace GS.Game.Tests {
	public class CompletionConditionTests {
		const string OrgA = "org-a";
		const string OrgB = "org-b";

		[Fact]
		void recursive_any_uses_configured_thresholds_and_either_leaf_can_qualify() {
			var config = Any(
				new CompletionConditionConfig { Type = "total_control", Value = 0.9 },
				Any(new CompletionConditionConfig { Type = "full_control_countries", Value = 3 }));
			ICompletionCondition condition = CompletionConditionFactory.Create(config, 100);
			var world = new World();
			AddControl(world, OrgA, "a", 100);
			AddControl(world, OrgA, "b", 100);
			AddControl(world, OrgA, "c", 70);

			Assert.True(condition.IsMet(Context(world, "a", "b", "c")));
			Assert.False(condition.IsMet(Context(world, "a", "b", "c", "d")));

			config.Members[0].Value = 0.5;
			condition = CompletionConditionFactory.Create(config, 100);
			Assert.True(condition.IsMet(Context(world, "a", "b", "c", "d")));
		}

		[Fact]
		void total_control_is_inclusive_at_point_eight_and_counts_zero_control_capacity() {
			var world = new World();
			AddControl(world, OrgA, "a", 100);
			AddControl(world, OrgA, "b", 60);
			var condition = new TotalControlCondition(0.8);

			Assert.True(condition.IsMet(Context(world, "a", "b")));
			Assert.False(condition.IsMet(Context(world, "a", "b", "zero")));
		}

		[Fact]
		void full_control_is_inclusive_at_fifteen_and_rejects_fourteen() {
			var world = new World();
			var countries = new List<string>();
			for (int i = 0; i < 15; i++) {
				string country = $"country-{i}";
				countries.Add(country);
				AddControl(world, OrgA, country, i == 14 ? 99 : 100);
			}
			var condition = new FullControlCondition(15);

			Assert.False(condition.IsMet(new CompletionConditionContext(world, OrgA, countries, 100)));
			AddControl(world, OrgA, countries[14], 1);
			Assert.True(condition.IsMet(new CompletionConditionContext(world, OrgA, countries, 100)));
		}

		[Fact]
		void leaves_sum_matching_contributions_and_exclude_other_orgs_and_unavailable_countries() {
			var world = new World();
			AddControl(world, OrgA, "available", 45);
			AddControl(world, OrgA, "available", 55);
			AddControl(world, OrgB, "available", 100);
			AddControl(world, OrgA, "unavailable", 100);
			CompletionConditionContext context = Context(world, "available", "zero");

			Assert.True(new FullControlCondition(1).IsMet(context));
			Assert.False(new FullControlCondition(2).IsMet(context));
			Assert.True(new TotalControlCondition(0.5).IsMet(context));
			Assert.False(new TotalControlCondition(0.51).IsMet(context));
		}

		[Fact]
		void leaves_fail_safely_when_no_countries_are_available() {
			var context = new CompletionConditionContext(new World(), OrgA, Array.Empty<string>(), 100);

			Assert.False(new TotalControlCondition(0.8).IsMet(context));
			Assert.False(new FullControlCondition(15).IsMet(context));
		}

		[Fact]
		void file_config_deserializes_camel_case_recursive_tree() {
			const string json = """
				{
					"completionCondition": {
						"type": "any",
						"value": 7,
						"members": [
							{ "type": "total_control", "value": 0.75 },
							{ "type": "any", "members": [
								{ "type": "full_control_countries", "value": 12 }
							] }
						]
					}
				}
				""";
			string path = Path.GetTempFileName();
			try {
				File.WriteAllText(path, json);
				GameSettings settings = new FileConfig<GameSettings>(path).Load();
				AssertRecursiveConfig(settings.CompletionCondition);
			} finally {
				File.Delete(path);
			}
		}

		[Fact]
		void newtonsoft_deserializes_camel_case_recursive_tree() {
			const string json = """
				{ "completionCondition": { "type": "any", "value": 7, "members": [
					{ "type": "total_control", "value": 0.75 },
					{ "type": "any", "members": [
						{ "type": "full_control_countries", "value": 12 }
					] }
				] } }
				""";

			GameSettings? settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			AssertRecursiveConfig(settings!.CompletionCondition);
		}

		[Fact]
		void absent_completion_key_uses_default_tree_in_both_serializers() {
			const string json = "{ \"startYear\": 1900 }";
			string path = Path.GetTempFileName();
			try {
				File.WriteAllText(path, json);
				GameSettings fileSettings = new FileConfig<GameSettings>(path).Load();
				GameSettings? newtonsoftSettings = JsonConvert.DeserializeObject<GameSettings>(json);

				AssertDefaultConfig(fileSettings.CompletionCondition);
				Assert.NotNull(newtonsoftSettings);
				AssertDefaultConfig(newtonsoftSettings!.CompletionCondition);
			} finally {
				File.Delete(path);
			}
		}

		[Fact]
		void factory_reports_context_for_null_and_invalid_recursive_nodes() {
			ArgumentException nullRoot = Assert.Throws<ArgumentException>(
				() => CompletionConditionFactory.Create(null!, 100));
			Assert.Contains("completionCondition", nullRoot.Message);

			ArgumentException nullMember = Assert.Throws<ArgumentException>(() =>
				CompletionConditionFactory.Create(new CompletionConditionConfig {
					Type = "any",
					Members = new List<CompletionConditionConfig> { null! }
				}, 100));
			Assert.Contains("completionCondition.members[0]", nullMember.Message);

			ArgumentException emptyAny = Assert.Throws<ArgumentException>(() =>
				CompletionConditionFactory.Create(new CompletionConditionConfig { Type = "any" }, 100));
			Assert.Contains("at least one member", emptyAny.Message);

			ArgumentException unknown = Assert.Throws<ArgumentException>(() =>
				CompletionConditionFactory.Create(new CompletionConditionConfig { Type = "mystery" }, 100));
			Assert.Contains("mystery", unknown.Message);
			Assert.Contains("completionCondition", unknown.Message);
		}

		[Theory]
		[InlineData("total_control", 0)]
		[InlineData("total_control", 1.01)]
		[InlineData("full_control_countries", 0)]
		[InlineData("full_control_countries", 1.5)]
		void factory_reports_context_for_invalid_thresholds(string type, double value) {
			ArgumentException exception = Assert.Throws<ArgumentException>(() =>
				CompletionConditionFactory.Create(new CompletionConditionConfig { Type = type, Value = value }, 100));

			Assert.Contains("completionCondition", exception.Message);
			Assert.Contains("Invalid completion condition", exception.Message);
		}

		[Fact]
		void factory_and_context_reject_non_positive_capacity() {
			Assert.Throws<ArgumentOutOfRangeException>(() => CompletionConditionFactory.Create(
				new CompletionConditionConfig { Type = "total_control", Value = 0.8 }, 0));
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				new CompletionConditionContext(new World(), OrgA, new[] { "a" }, 0));
		}

		static CompletionConditionConfig Any(params CompletionConditionConfig[] members) {
			return new CompletionConditionConfig {
				Type = "any",
				Members = new List<CompletionConditionConfig>(members)
			};
		}

		static CompletionConditionContext Context(World world, params string[] countries) {
			return new CompletionConditionContext(world, OrgA, countries, 100);
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = $"{orgId}-{countryId}-{entity}"
			});
		}

		static void AssertRecursiveConfig(CompletionConditionConfig config) {
			Assert.Equal("any", config.Type);
			Assert.Equal(7, config.Value);
			Assert.Equal(2, config.Members.Count);
			Assert.Equal("total_control", config.Members[0].Type);
			Assert.Equal(0.75, config.Members[0].Value);
			Assert.Equal("any", config.Members[1].Type);
			Assert.Single(config.Members[1].Members);
			Assert.Equal("full_control_countries", config.Members[1].Members[0].Type);
			Assert.Equal(12, config.Members[1].Members[0].Value);
		}

		static void AssertDefaultConfig(CompletionConditionConfig config) {
			Assert.Equal("any", config.Type);
			Assert.Collection(config.Members,
				member => {
					Assert.Equal("total_control", member.Type);
					Assert.Equal(0.8, member.Value);
				},
				member => {
					Assert.Equal("full_control_countries", member.Type);
					Assert.Equal(15, member.Value);
				});
		}
	}
}
