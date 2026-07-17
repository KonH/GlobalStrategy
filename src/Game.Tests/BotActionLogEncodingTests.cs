using System;
using System.IO;
using ECS;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotActionLogEncodingTests {
		static string FindRepoRoot() {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "Configs"))) {
				dir = dir.Parent;
			}
			if (dir == null) {
				throw new InvalidOperationException("Could not locate repo root containing Assets/Configs.");
			}
			return dir.FullName;
		}

		[Fact]
		void bot_action_log_entries_without_delimiter_round_trip_byte_for_byte() {
			var world = new World();
			int entity = world.Create();
			var entries = new[] {
				"2026-07-17T00:00:00.0000000Z\x1EIlluminati\x1EDiscoverAndControl\x1Espread_rumors\x1EFrance",
				"2026-07-18T00:00:00.0000000Z\x1EIlluminati\x1EDiscoverAndControl\x1Espend_gold\x1E"
			};
			world.Add(entity, new BotActionLog { Entries = entries });

			var snapshot = SaveSystem.BuildSnapshot(world);
			var restored = new World();
			LoadSystem.Apply(snapshot, restored);

			int[] req = { TypeId<BotActionLog>.Value };
			string[]? restoredEntries = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					restoredEntries = arch.GetColumn<BotActionLog>()[0].Entries;
					break;
				}
			}

			Assert.NotNull(restoredEntries);
			Assert.Equal(entries, restoredEntries);
		}

		[Fact]
		void organization_ids_never_contain_the_bot_action_log_delimiter() {
			string root = FindRepoRoot();
			string json = File.ReadAllText(Path.Combine(root, "Assets", "Configs", "organizations.json"));
			Assert.DoesNotContain('\x1E', json);
		}

		[Fact]
		void action_ids_never_contain_the_bot_action_log_delimiter() {
			string root = FindRepoRoot();
			string json = File.ReadAllText(Path.Combine(root, "Assets", "Configs", "action_config.json"));
			Assert.DoesNotContain('\x1E', json);
		}

		[Fact]
		void country_ids_never_contain_the_bot_action_log_delimiter() {
			string root = FindRepoRoot();
			string json = File.ReadAllText(Path.Combine(root, "Assets", "Configs", "country_config.json"));
			Assert.DoesNotContain('\x1E', json);
		}
	}
}
