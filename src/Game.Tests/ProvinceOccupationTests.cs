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
	public class ProvinceOccupationTests {
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
			public IReadOnlyList<string> List(string dir) => new List<string>();
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
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = "Great_Britain", Population = 1234.0 },
					new ProvinceEntry { ProvinceId = "prov_b", CountryId = "France", Population = 5678.0 }
				}
			};
			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(new ResourceConfig { Resources = new List<ResourceDefinition>() }),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				storage: storage,
				serializer: serializer,
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

		[Fact]
		void seed_creates_unoccupied_entry_for_each_province() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(2, CountEntities<ProvinceOccupation>(logic.World));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_a"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_b"));
			Assert.Empty(ProvinceOccupationSystem.GetOccupierByProvinceId(logic.World));
		}

		[Fact]
		void set_occupier_changes_runtime_state_without_changing_owner() {
			var logic = BuildLogic();
			logic.Update(0f);

			var (changed, oldOccupierId) = ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", "Great_Britain");

			Assert.True(changed);
			Assert.Equal("", oldOccupierId);
			Assert.Equal("Great_Britain", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_b"));
			Assert.Equal("France", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_b"));
		}

		[Fact]
		void setting_same_occupier_is_noop_and_does_not_bump_version() {
			var logic = BuildLogic();
			logic.Update(0f);
			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", "Great_Britain");
			int version = ProvinceOccupationSystem.GetVersion(logic.World);

			var (changed, oldOccupierId) = ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", "Great_Britain");

			Assert.False(changed);
			Assert.Equal("", oldOccupierId);
			Assert.Equal(version, ProvinceOccupationSystem.GetVersion(logic.World));
		}

		[Fact]
		void clear_occupier_returns_to_unoccupied() {
			var logic = BuildLogic();
			logic.Update(0f);
			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", "Great_Britain");

			var (changed, oldOccupierId) = ProvinceOccupationSystem.ClearOccupier(logic.World, "prov_b");

			Assert.True(changed);
			Assert.Equal("Great_Britain", oldOccupierId);
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_b"));
		}

		[Fact]
		void toggle_sets_when_absent_and_clears_when_same_occupier() {
			var logic = BuildLogic();
			logic.Update(0f);

			var set = ProvinceOccupationSystem.ToggleOccupier(logic.World, "prov_b", "Great_Britain");
			var clear = ProvinceOccupationSystem.ToggleOccupier(logic.World, "prov_b", "Great_Britain");

			Assert.True(set.Changed);
			Assert.Equal("Great_Britain", set.NewOccupierId);
			Assert.True(clear.Changed);
			Assert.Equal("Great_Britain", clear.OldOccupierId);
			Assert.Equal("", clear.NewOccupierId);
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_b"));
		}

		[Fact]
		void debug_command_toggles_occupation_through_game_logic() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugToggleProvinceOccupationCommand { ProvinceId = "prov_b", OccupierId = "Great_Britain" });
			logic.Update(0f);
			Assert.Equal("Great_Britain", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_b"));

			logic.Commands.Push(new DebugToggleProvinceOccupationCommand { ProvinceId = "prov_b", OccupierId = "Great_Britain" });
			logic.Update(0f);
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(logic.World, "prov_b"));
		}

		[Fact]
		void visual_state_updates_when_occupation_changes() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugToggleProvinceOccupationCommand { ProvinceId = "prov_b", OccupierId = "Great_Britain" });
			logic.Update(0f);

			Assert.True(logic.VisualState.ProvinceOccupation.OccupierByProvinceId.TryGetValue("prov_b", out string occupierId));
			Assert.Equal("Great_Britain", occupierId);
			Assert.Equal("prov_b", logic.VisualState.ProvinceOccupation.RecentProvinceId);
			Assert.Equal("", logic.VisualState.ProvinceOccupation.RecentOldOccupierId);
			Assert.Equal("Great_Britain", logic.VisualState.ProvinceOccupation.RecentNewOccupierId);
		}

		[Fact]
		void occupation_round_trips_through_save_load() {
			var storage = new MemoryStorage();
			var serializer = new CapturingSerializer();
			var logic = BuildLogic(storage, serializer);
			logic.Update(0f);
			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", "Great_Britain");
			logic.Commands.Push(new SaveGameCommand());
			logic.Update(0f);

			var loadedLogic = BuildLogic(storage, serializer);
			loadedLogic.LoadState(serializer.LastSaveName);
			loadedLogic.Update(0f);

			Assert.Equal("Great_Britain", ProvinceOccupationSystem.GetOccupier(loadedLogic.World, "prov_b"));
		}

		[Fact]
		void owner_aggregates_ignore_occupation() {
			var logic = BuildLogic();
			logic.Update(0f);

			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", "Great_Britain");
			var byOwner = ProvinceOwnershipSystem.GetProvincesByOwner(logic.World);

			Assert.True(byOwner.TryGetValue("France", out var franceProvinces));
			Assert.Contains("prov_b", franceProvinces);
			Assert.False(byOwner.TryGetValue("Great_Britain", out var gbProvinces) && gbProvinces.Contains("prov_b"));
		}
	}
}
