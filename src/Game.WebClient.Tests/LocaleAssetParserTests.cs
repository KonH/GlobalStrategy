using System;
using System.Collections.Generic;
using System.IO;
using GS.Game.WebClient.LocaleTool;
using Xunit;

namespace GS.Game.WebClient.Tests {
	public class LocaleAssetParserTests {
		// dotnet test runs from the test assembly's own output directory, not the repo root -
		// walk up until Assets/Localization is found, same pattern as StringConfigParityTests.
		static string FindRepoRootLocalizationPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Localization", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Localization/{fileName} above {AppContext.BaseDirectory}.");
		}

		[Fact]
		void parses_bare_value() {
			string content = File.ReadAllText(FindRepoRootLocalizationPath("en.asset"));
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);
			Assert.Equal("Play", entries["menu.play"]);
		}

		[Fact]
		void unquotes_double_quoted_value() {
			string content = File.ReadAllText(FindRepoRootLocalizationPath("en.asset"));
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);
			Assert.Equal("{0} discovered {1}", entries["game_log.discovered_format"]);
		}

		[Fact]
		void unquotes_single_quoted_value() {
			string content = File.ReadAllText(FindRepoRootLocalizationPath("en.asset"));
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);
			Assert.Equal("Error happens while saving game: {0}", entries["game_menu.save.error"]);
		}

		[Fact]
		void unescapes_unicode_sequences_in_ru_asset() {
			string content = File.ReadAllText(FindRepoRootLocalizationPath("ru.asset"));
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);
			Assert.Equal("Хоругх", entries["province_name.central_Asian_khanates__Khorugh"]);
		}

		[Fact]
		void finds_new_web_keys_in_en_asset() {
			string content = File.ReadAllText(FindRepoRootLocalizationPath("en.asset"));
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);
			Assert.Equal(
				"This is an experimental web version of the game; it may be unstable and lose data.",
				entries["web.header.warning"]);
			Assert.Equal("Play the full client", entries["web.header.full_client_link_text"]);
			Assert.Equal("© 2026 KonH · MIT License", entries["web.footer.copyright"]);
			Assert.Equal("View source on GitHub", entries["web.footer.github_link_text"]);
		}

		[Fact]
		void finds_new_web_keys_in_ru_asset() {
			string content = File.ReadAllText(FindRepoRootLocalizationPath("ru.asset"));
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);
			Assert.Equal(
				"Это экспериментальная веб-версия игры; она может быть нестабильной и терять данные.",
				entries["web.header.warning"]);
			Assert.Equal("Играть в полную версию", entries["web.header.full_client_link_text"]);
			Assert.Equal("© 2026 KonH · Лицензия MIT", entries["web.footer.copyright"]);
			Assert.Equal("Исходный код на GitHub", entries["web.footer.github_link_text"]);
		}

		[Fact]
		void malformed_input_is_skipped_without_throwing() {
			string content =
				"Entries:\n" +
				"  - Key: only.key.no.value\n" +
				"  - Key: good.key\n" +
				"    Value: Good Value\n" +
				"    Value: orphan.value.no.key\n" +
				"  - Key:\n" +
				"    Value: empty.key.value\n";

			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);

			Assert.Equal("Good Value", entries["good.key"]);
			Assert.False(entries.ContainsKey("only.key.no.value"));
			Assert.Single(entries);
		}

		[Fact]
		void empty_input_returns_empty_map() {
			Dictionary<string, string> entries = LocaleAssetParser.Parse(string.Empty);
			Assert.Empty(entries);
		}
	}
}
