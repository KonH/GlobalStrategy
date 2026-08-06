using GS.Game.Configs;
using Newtonsoft.Json;
using Xunit;

namespace GS.Game.Tests {
	public class FeatureFlagSettingsTests {
		[Fact]
		void featureFlags_block_round_trips_from_json() {
			const string json = @"{
				""startYear"": 1880,
				""defaultLocale"": ""en"",
				""autoSaveInterval"": ""monthly"",
				""featureFlags"": {
					""showPlayerOrgControls"": false
				}
			}";

			var settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			Assert.False(settings!.FeatureFlags.ShowPlayerOrgControls);
		}

		[Fact]
		void featureFlags_defaults_apply_when_block_absent_from_json() {
			const string json = @"{
				""startYear"": 1880,
				""defaultLocale"": ""en"",
				""autoSaveInterval"": ""monthly""
			}";

			var settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			Assert.True(settings!.FeatureFlags.ShowPlayerOrgControls);
		}

		[Fact]
		void featureFlagSettings_class_default_is_true() {
			var settings = new FeatureFlagSettings();
			Assert.True(settings.ShowPlayerOrgControls);
		}

		[Fact]
		void enableSecretAdvisor_round_trips_from_json() {
			const string json = @"{
				""startYear"": 1880,
				""defaultLocale"": ""en"",
				""autoSaveInterval"": ""monthly"",
				""featureFlags"": {
					""enableSecretAdvisor"": true
				}
			}";

			var settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			Assert.True(settings!.FeatureFlags.EnableSecretAdvisor);
		}

		[Fact]
		void enableSecretAdvisor_defaults_to_false_when_absent_from_json() {
			const string json = @"{
				""startYear"": 1880,
				""defaultLocale"": ""en"",
				""autoSaveInterval"": ""monthly""
			}";

			var settings = JsonConvert.DeserializeObject<GameSettings>(json);

			Assert.NotNull(settings);
			Assert.False(settings!.FeatureFlags.EnableSecretAdvisor);
		}

		[Fact]
		void featureFlagSettings_class_default_enableSecretAdvisor_is_false() {
			var settings = new FeatureFlagSettings();
			Assert.False(settings.EnableSecretAdvisor);
		}
	}
}
