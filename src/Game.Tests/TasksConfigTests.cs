using System.Collections.Generic;
using GS.Configs.IO;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class TasksConfigTests {
		[Fact]
		void empty_json_deserializes_to_empty_tasks_list() {
			var config = new StringConfig<TasksConfig>("{ \"tasks\": [] }").Load();
			Assert.Empty(config.Tasks);
		}

		[Fact]
		void full_sample_deserializes_fields_and_defaults_missing_optionals() {
			const string json = @"
{
  ""tasks"": [
    {
      ""taskId"": ""task_a"",
      ""nameKey"": ""task.a.name"",
      ""descKey"": ""task.a.desc"",
      ""openCondition"": { ""type"": ""value"", ""value"": 1 },
      ""closeCondition"": { ""type"": ""gte"", ""members"": [
        { ""type"": ""triggerCondition"", ""triggerId"": ""done"" },
        { ""type"": ""value"", ""value"": 1 }
      ]},
      ""reward"": [ { ""resourceId"": ""gold"", ""amount"": 25 } ],
      ""openEffectIds"": [ ""grant_gold_effect"" ],
      ""closeEffectIds"": [ ""grant_gold_effect"" ]
    },
    {
      ""taskId"": ""task_b"",
      ""nameKey"": ""task.b.name"",
      ""descKey"": ""task.b.desc"",
      ""openCondition"": { ""type"": ""value"", ""value"": 0 },
      ""closeCondition"": { ""type"": ""value"", ""value"": 0 }
    }
  ]
}";
			var config = new StringConfig<TasksConfig>(json).Load();
			Assert.Equal(2, config.Tasks.Count);

			TaskDefinition? a = config.Find("task_a");
			Assert.NotNull(a);
			Assert.Equal("task.a.name", a!.NameKey);
			Assert.Equal("task.a.desc", a.DescKey);
			Assert.NotNull(a.OpenCondition);
			Assert.NotNull(a.CloseCondition);
			Assert.Single(a.Reward);
			Assert.Equal(ResourceDefinitions.Gold, a.Reward[0].ResourceId);
			Assert.Equal(25, a.Reward[0].Amount);
			Assert.Equal(new List<string> { "grant_gold_effect" }, a.OpenEffectIds);
			Assert.Equal(new List<string> { "grant_gold_effect" }, a.CloseEffectIds);

			TaskDefinition? b = config.Find("task_b");
			Assert.NotNull(b);
			Assert.Empty(b!.Reward);
			Assert.Empty(b.OpenEffectIds);
			Assert.Empty(b.CloseEffectIds);
			Assert.False(b.IsTutorial);
			Assert.Equal("", b.HighlightTargetId);
		}

		[Fact]
		void tutorial_fields_deserialize_and_default_when_missing() {
			const string json = @"
{
  ""tasks"": [
    {
      ""taskId"": ""tutorial_welcome_camera"",
      ""nameKey"": ""task.tutorial_welcome_camera.name"",
      ""descKey"": ""task.tutorial_welcome_camera.desc"",
      ""isTutorial"": true,
      ""highlightTargetId"": ""player_org_panel"",
      ""openCondition"": { ""type"": ""value"", ""value"": 1 },
      ""closeCondition"": { ""type"": ""value"", ""value"": 1 }
    },
    {
      ""taskId"": ""plain_task"",
      ""nameKey"": ""task.plain.name"",
      ""descKey"": ""task.plain.desc"",
      ""openCondition"": { ""type"": ""value"", ""value"": 0 },
      ""closeCondition"": { ""type"": ""value"", ""value"": 0 }
    }
  ]
}";
			var config = new StringConfig<TasksConfig>(json).Load();
			TaskDefinition? tutorial = config.Find("tutorial_welcome_camera");
			Assert.NotNull(tutorial);
			Assert.True(tutorial!.IsTutorial);
			Assert.Equal("player_org_panel", tutorial.HighlightTargetId);

			TaskDefinition? plain = config.Find("plain_task");
			Assert.NotNull(plain);
			Assert.False(plain!.IsTutorial);
			Assert.Equal("", plain.HighlightTargetId);
		}
	}
}
