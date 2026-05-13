using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class InitSystemTests {
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

		static GameLogic BuildLogic(IPersistentStorage? storage = null, ISnapshotSerializer? serializer = null) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true }
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

		static int CountEntities<T>(World world) {
			int count = 0;
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				count += arch.Count;
			}
			return count;
		}

		[Fact]
		void world_is_empty_before_first_update() {
			var logic = BuildLogic();
			Assert.Equal(0, CountEntities<Country>(logic.World));
		}

		[Fact]
		void world_is_populated_after_first_update() {
			var logic = BuildLogic();
			logic.Update(0f);
			Assert.True(CountEntities<Country>(logic.World) > 0);
		}

		[Fact]
		void init_does_not_run_twice() {
			var logic = BuildLogic();
			logic.Update(0f);
			int countFirst = CountEntities<Country>(logic.World);
			logic.Update(0f);
			Assert.Equal(countFirst, CountEntities<Country>(logic.World));
		}

		[Fact]
		void init_skipped_after_load() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var logic = BuildLogic(storage, serializer);

			logic.Update(0f);
			int countAfterInit = CountEntities<Country>(logic.World);

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);

			string saveName = serializer.LastSaveName;
			logic.LoadState(saveName);
			logic.Update(0f);

			Assert.Equal(countAfterInit, CountEntities<Country>(logic.World));
		}
	}
}
