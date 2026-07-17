using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotActionLogTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		sealed class MemoryStorage : IPersistentStorage {
			readonly Dictionary<string, string> _files = new Dictionary<string, string>();
			public void Write(string path, string content) => _files[path] = content;
			public string Read(string path) => _files[path];
			public bool Exists(string path) => _files.ContainsKey(path);
			public void Delete(string path) => _files.Remove(path);
			public IReadOnlyList<string> List(string dir) {
				var result = new List<string>();
				string prefix = dir + "/";
				foreach (var key in _files.Keys) {
					if (key.StartsWith(prefix)) {
						result.Add(key.Substring(prefix.Length));
					}
				}
				return result;
			}
		}

		sealed class CapturingSerializer : ISnapshotSerializer {
			readonly Dictionary<string, WorldSnapshot> _store = new Dictionary<string, WorldSnapshot>();
			public string LastSaveName { get; private set; } = "";

			public string Serialize(WorldSnapshot snapshot) {
				LastSaveName = snapshot.Header.SaveName;
				_store[snapshot.Header.SaveName] = snapshot;
				return snapshot.Header.SaveName;
			}

			public WorldSnapshot Deserialize(string json) => _store[json];
		}

		static GameLogic BuildLogic(
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null,
			int? botActionLogRetentionCap = null) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					}
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly"
			};
			if (botActionLogRetentionCap.HasValue) {
				gameSettings.BotActionLogRetentionCap = botActionLogRetentionCap.Value;
			}
			var resourceConfig = new ResourceConfig { Resources = new List<ResourceDefinition>() };
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				storage: storage,
				serializer: serializer,
				initialPlayerCountryId: "Great_Britain",
				initialOrganizationId: "Illuminati");
			return new GameLogic(ctx);
		}

		static string[] ReadBotActionLogEntries(World world) {
			int[] req = { TypeId<BotActionLog>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<BotActionLog>()[0].Entries;
				}
			}
			return System.Array.Empty<string>();
		}

		[Fact]
		void bot_action_log_singleton_exists_with_empty_entries_after_init() {
			var logic = BuildLogic();
			logic.Update(0f);

			var entries = ReadBotActionLogEntries(logic.World);
			Assert.NotNull(entries);
			Assert.Empty(entries);
		}

		[Fact]
		void record_bot_action_appends_one_correctly_delimited_entry() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.RecordBotAction("Illuminati", "DiscoverAndControl", "spread_rumors", "France");

			var entries = ReadBotActionLogEntries(logic.World);
			Assert.Single(entries);

			var parts = entries[0].Split('\x1E');
			Assert.Equal(5, parts.Length);
			Assert.Equal("Illuminati", parts[1]);
			Assert.Equal("DiscoverAndControl", parts[2]);
			Assert.Equal("spread_rumors", parts[3]);
			Assert.Equal("France", parts[4]);
		}

		[Fact]
		void record_bot_action_increments_entries_length_by_one_per_call() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.RecordBotAction("Illuminati", "Feature1", "action1", "France");
			Assert.Single(ReadBotActionLogEntries(logic.World));

			logic.RecordBotAction("Illuminati", "Feature1", "action2", "France");
			Assert.Equal(2, ReadBotActionLogEntries(logic.World).Length);

			logic.RecordBotAction("Illuminati", "Feature1", "action3", "France");
			Assert.Equal(3, ReadBotActionLogEntries(logic.World).Length);
		}

		[Fact]
		void record_bot_action_trims_from_front_once_retention_cap_exceeded() {
			var logic = BuildLogic(botActionLogRetentionCap: 3);
			logic.Update(0f);

			for (int i = 0; i < 5; i++) {
				logic.RecordBotAction("Illuminati", "Feature1", $"action{i}", "France");
			}

			var entries = ReadBotActionLogEntries(logic.World);
			Assert.Equal(3, entries.Length);

			var actionIds = new List<string>();
			foreach (var entry in entries) {
				actionIds.Add(entry.Split('\x1E')[3]);
			}
			Assert.Equal(new[] { "action2", "action3", "action4" }, actionIds);
		}

		[Fact]
		void record_bot_action_continues_appending_after_save_load_round_trip() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var logic = BuildLogic(storage, serializer);

			logic.Update(0f);
			logic.RecordBotAction("Illuminati", "Feature1", "action1", "France");
			Assert.Single(ReadBotActionLogEntries(logic.World));

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			string saveName = serializer.LastSaveName;

			var loadedLogic = BuildLogic(storage, serializer);
			loadedLogic.LoadState(saveName);

			var restoredEntries = ReadBotActionLogEntries(loadedLogic.World);
			Assert.Single(restoredEntries);

			loadedLogic.RecordBotAction("Illuminati", "Feature1", "action2", "France");
			var entriesAfterAppend = ReadBotActionLogEntries(loadedLogic.World);
			Assert.Equal(2, entriesAfterAppend.Length);
			Assert.Equal(restoredEntries[0], entriesAfterAppend[0]);
		}
	}
}
