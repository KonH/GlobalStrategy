using GS.Game.Configs;
using Newtonsoft.Json;
using Xunit;

namespace GS.Game.Tests {
	public class GameLogSettingsTests {
		[Fact]
		void gameLog_block_round_trips_from_json() {
			const string json = @"{
				""startYear"": 1880,
				""defaultLocale"": ""en"",
				""autoSaveInterval"": ""monthly"",
				""gameLog"": {
					""includePlayerActions"": false,
					""maxLogEntries"": 5
				}
			}";

			var settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			Assert.False(settings!.GameLog.IncludePlayerActions);
			Assert.Equal(5, settings.GameLog.MaxLogEntries);
		}

		[Fact]
		void gameLog_defaults_apply_when_block_absent_from_json() {
			const string json = @"{
				""startYear"": 1880,
				""defaultLocale"": ""en"",
				""autoSaveInterval"": ""monthly""
			}";

			var settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			Assert.True(settings!.GameLog.IncludePlayerActions);
			Assert.Equal(12, settings.GameLog.MaxLogEntries);
		}

		[Fact]
		void gameLogSettings_class_defaults_are_true_and_twelve() {
			var settings = new GameLogSettings();
			Assert.True(settings.IncludePlayerActions);
			Assert.Equal(12, settings.MaxLogEntries);
		}
	}
}
