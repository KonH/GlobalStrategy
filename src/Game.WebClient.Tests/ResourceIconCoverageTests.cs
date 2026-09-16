using System;
using System.IO;
using GS.Configs.IO;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.WebClient.Tests {
	// Icon-coverage guard replacing the abandoned runtime probe (see plan's Approach
	// section) - every displayWhitelist id must have both an artwork file and a matching
	// .resource-icon--{id} USS rule, checked deterministically at dotnet test time.
	public class ResourceIconCoverageTests {
		static string FindRepoRootPath(string relativePath) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, relativePath);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate {relativePath} above {AppContext.BaseDirectory}.");
		}

		static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}

		[Fact]
		void every_whitelisted_resource_has_artwork_and_uss_rule() {
			ResourceConfig config = new FileConfig<ResourceConfig>(FindRepoRootConfigPath("resources.json")).Load();
			string ussPath = FindRepoRootPath(Path.Combine("Assets", "UI", "Shared", "SharedStyles.uss"));
			string uss = File.ReadAllText(ussPath);
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "Textures", "Icons", "ResourceRow"))) {
				dir = dir.Parent;
			}
			if (dir == null) {
				throw new InvalidOperationException("Could not locate Assets/Textures/Icons/ResourceRow above " + AppContext.BaseDirectory);
			}
			string iconDir = Path.Combine(dir.FullName, "Assets", "Textures", "Icons", "ResourceRow");

			foreach (string resourceId in config.DisplayWhitelist) {
				string iconPath = Path.Combine(iconDir, resourceId + ".png");
				Assert.True(File.Exists(iconPath), $"Missing artwork '{iconPath}'");

				string selector = "." + ResourceDisplayNaming.IconClass(resourceId);
				Assert.Contains(selector, uss);
			}
		}
	}
}
