using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
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
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = "Great_Britain", Population = 1234.0 },
					new ProvinceEntry { ProvinceId = "prov_b", CountryId = "France", Population = 5678.0 }
				}
			};

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				storage: storage,
				serializer: serializer,
				initialOrganizationId: "Illuminati",
				province: new StaticConfig<ProvinceConfig>(provinceConfig));
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

		static double? GetResourceValue(World world, string ownerId, string resourceId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return resources[i].Value;
					}
				}
			}
			return null;
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

		[Fact]
		void province_ownership_seeded_once_from_config() {
			var logic = BuildLogic();
			logic.Update(0f);
			int countAfterInit = CountEntities<ProvinceOwnership>(logic.World);
			Assert.Equal(2, countAfterInit);

			logic.Update(0f);
			Assert.Equal(countAfterInit, CountEntities<ProvinceOwnership>(logic.World));
		}

		[Fact]
		void province_population_seeded_from_config() {
			var logic = BuildLogic();
			logic.Update(0f);

			var populationByProvinceId = new Dictionary<string, (double Value, OwnerType OwnerType)>();
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (resources[i].ResourceId == "population") {
						populationByProvinceId[owners[i].OwnerId] = (resources[i].Value, owners[i].OwnerType);
					}
				}
			}

			Assert.Equal(2, populationByProvinceId.Count);
			Assert.Equal((1234.0, OwnerType.Province), populationByProvinceId["prov_a"]);
			Assert.Equal((5678.0, OwnerType.Province), populationByProvinceId["prov_b"]);
		}

		[Fact]
		void country_population_and_score_seeded_at_init_from_province_population() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(1234.0, GetResourceValue(logic.World, "Great_Britain", "country_population"));
			Assert.Equal(5678.0, GetResourceValue(logic.World, "France", "country_population"));
			Assert.Equal(1234.0, CountryScoreSystem.GetScore(logic.World, "Great_Britain"));
			Assert.Equal(5678.0, CountryScoreSystem.GetScore(logic.World, "France"));
		}

		[Fact]
		void country_score_correct_immediately_after_load_with_no_forced_recompute() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var logic = BuildLogic(storage, serializer);

			logic.Update(0f);
			double scoreGBBeforeSave = CountryScoreSystem.GetScore(logic.World, "Great_Britain");
			double scoreFRBeforeSave = CountryScoreSystem.GetScore(logic.World, "France");
			Assert.Equal(1234.0, scoreGBBeforeSave);
			Assert.Equal(5678.0, scoreFRBeforeSave);

			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);
			string saveName = serializer.LastSaveName;

			var loadedLogic = BuildLogic(storage, serializer);
			loadedLogic.LoadState(saveName);

			Assert.Equal(scoreGBBeforeSave, CountryScoreSystem.GetScore(loadedLogic.World, "Great_Britain"));
			Assert.Equal(scoreFRBeforeSave, CountryScoreSystem.GetScore(loadedLogic.World, "France"));
		}

		[Fact]
		void province_population_unaffected_on_first_tick() {
			var logic = BuildLogic();
			logic.Update(0f);

			double? populationA = null;
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == "prov_a" && resources[i].ResourceId == "population") {
						populationA = resources[i].Value;
					}
				}
			}

			Assert.Equal(1234.0, populationA);
		}

		[Fact]
		void province_population_growth_still_compounds_monthly() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Update(744f); // Jan1 -> Feb1 (31 days), crosses one month boundary
			double afterFirstMonth = GetResourceValue(logic.World, "prov_a", "population")!.Value;
			Assert.Equal(1234.0 * 1.00075, afterFirstMonth, 6);

			logic.Update(696f); // Feb1 -> Mar1 (1880 is a leap year: 29 days), crosses a second boundary
			double afterSecondMonth = GetResourceValue(logic.World, "prov_a", "population")!.Value;
			Assert.Equal(afterFirstMonth * 1.00075, afterSecondMonth, 6);
		}
	}
}
