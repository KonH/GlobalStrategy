using System;
using System.Collections.Generic;
using System.IO;
using GS.Configs.IO;
using GS.Game.Configs;
using GS.Game.WebClient.LocaleTool;
using Xunit;

namespace GS.Game.WebClient.Tests {
	// Text-coverage guard for the derived resource/effect naming convention
	// (ResourceDisplayNaming) - every displayWhitelist id must have a translated
	// resource.{id}.name / .description in both locales, and the removed
	// damage/durability entries must not linger in either locale.
	public class ResourceLocaleParityTests {
		static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}

		static string FindRepoRootLocalizationPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Localization", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Localization/{fileName} above {AppContext.BaseDirectory}.");
		}

		static Dictionary<string, string> LoadLocale(string fileName) {
			return LocaleAssetParser.Parse(File.ReadAllText(FindRepoRootLocalizationPath(fileName)));
		}

		[Fact]
		void every_whitelisted_resource_has_translated_name_and_description_in_both_locales() {
			ResourceConfig config = new FileConfig<ResourceConfig>(FindRepoRootConfigPath("resources.json")).Load();
			Dictionary<string, string> en = LoadLocale("en.asset");
			Dictionary<string, string> ru = LoadLocale("ru.asset");

			foreach (string resourceId in config.DisplayWhitelist) {
				string nameKey = ResourceDisplayNaming.NameKey(resourceId);
				string descriptionKey = ResourceDisplayNaming.DescriptionKey(resourceId);

				Assert.True(en.ContainsKey(nameKey), $"Missing '{nameKey}' in en.asset");
				Assert.True(ru.ContainsKey(nameKey), $"Missing '{nameKey}' in ru.asset");
				Assert.True(en.ContainsKey(descriptionKey), $"Missing '{descriptionKey}' in en.asset");
				Assert.True(ru.ContainsKey(descriptionKey), $"Missing '{descriptionKey}' in ru.asset");
			}
		}

		[Fact]
		void never_displayed_damage_and_durability_locale_entries_are_absent_from_both_locales() {
			Dictionary<string, string> en = LoadLocale("en.asset");
			Dictionary<string, string> ru = LoadLocale("ru.asset");

			Assert.False(en.ContainsKey(ResourceDisplayNaming.NameKey(ResourceDefinitions.Damage)));
			Assert.False(en.ContainsKey(ResourceDisplayNaming.DescriptionKey(ResourceDefinitions.Damage)));
			Assert.False(ru.ContainsKey(ResourceDisplayNaming.NameKey(ResourceDefinitions.Damage)));
			Assert.False(ru.ContainsKey(ResourceDisplayNaming.DescriptionKey(ResourceDefinitions.Damage)));

			Assert.False(en.ContainsKey(ResourceDisplayNaming.NameKey(ResourceDefinitions.Durability)));
			Assert.False(en.ContainsKey(ResourceDisplayNaming.DescriptionKey(ResourceDefinitions.Durability)));
			Assert.False(ru.ContainsKey(ResourceDisplayNaming.NameKey(ResourceDefinitions.Durability)));
			Assert.False(ru.ContainsKey(ResourceDisplayNaming.DescriptionKey(ResourceDefinitions.Durability)));
		}
	}
}
