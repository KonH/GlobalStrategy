using System;
using System.Linq;
using ECS;
using ECS.Extensions;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SaveLoadRoundTripTests {
		static World BuildWorld() {
			var world = new World();

			// Country with Player + IsSelected
			int country1 = world.Create();
			world.Add(country1, new Country("Russian_Empire"));
			world.Add(country1, new Player());
			world.Add(country1, new IsSelected());

			// Another country without components
			int country2 = world.Create();
			world.Add(country2, new Country("Ottoman_Empire"));

			// GameTime singleton
			int timeEntity = world.Create();
			world.Add(timeEntity, new GameTime {
				CurrentTime = new DateTime(1882, 6, 15),
				IsPaused = false,
				MultiplierIndex = 1,
				AccumulatedHours = 0.5f
			});

			// Locale
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });

			// AppSettings
			int settingsEntity = world.Create();
			world.Add(settingsEntity, new AppSettings {
				Locale = "en",
				AutoSaveInterval = AutoSaveInterval.Monthly
			});

			// Resource entity
			int resEntity = world.Create();
			world.Add(resEntity, new ResourceOwner("Russian_Empire"));
			world.Add(resEntity, new Resource { ResourceId = "gold", Value = 123.45 });

			// Effect entity
			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner("Russian_Empire"));
			world.Add(effectEntity, new ResourceLink("gold"));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = "gold_income",
				Value = 10.0,
				PayType = PayType.Monthly
			});

			// Organization entity
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Illuminati", DisplayName = "Illuminati" });

			return world;
		}

		static WorldSnapshot Snapshot(World world) => SaveSystem.BuildSnapshot(world);

		static void Restore(WorldSnapshot snapshot, World world) => LoadSystem.Apply(snapshot, world);

		[Fact]
		void round_trip_preserves_country_entities() {
			var original = BuildWorld();
			var snapshot = Snapshot(original);
			var restored = new World();
			Restore(snapshot, restored);

			var countries = new System.Collections.Generic.List<string>();
			int[] req = { TypeId<Country>.Value };
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				for (int i = 0; i < arch.Count; i++) {
					countries.Add(arch.GetColumn<Country>()[i].CountryId);
				}
			}

			Assert.Contains("Russian_Empire", countries);
			Assert.Contains("Ottoman_Empire", countries);
		}

		[Fact]
		void round_trip_preserves_player_component() {
			var original = BuildWorld();
			var snapshot = Snapshot(original);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<Country>.Value, TypeId<Player>.Value };
			string? playerCountryId = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					playerCountryId = arch.GetColumn<Country>()[0].CountryId;
					break;
				}
			}

			Assert.Equal("Russian_Empire", playerCountryId);
		}

		[Fact]
		void round_trip_preserves_game_time() {
			var original = BuildWorld();
			var snapshot = Snapshot(original);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<GameTime>.Value };
			GameTime? time = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					time = arch.GetColumn<GameTime>()[0];
					break;
				}
			}

			Assert.NotNull(time);
			Assert.Equal(new DateTime(1882, 6, 15), time.Value.CurrentTime);
			Assert.Equal(1, time.Value.MultiplierIndex);
			Assert.Equal(0.5f, time.Value.AccumulatedHours);
		}

		[Fact]
		void round_trip_preserves_resource_value() {
			var original = BuildWorld();
			var snapshot = Snapshot(original);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			double? goldValue = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				for (int i = 0; i < arch.Count; i++) {
					if (arch.GetColumn<Resource>()[i].ResourceId == "gold") {
						goldValue = arch.GetColumn<Resource>()[i].Value;
					}
				}
			}

			Assert.Equal(123.45, goldValue);
		}

		[Fact]
		void round_trip_preserves_enum_field() {
			var original = BuildWorld();
			var snapshot = Snapshot(original);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<ResourceEffect>.Value };
			PayType? payType = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					payType = arch.GetColumn<ResourceEffect>()[0].PayType;
					break;
				}
			}

			Assert.Equal(PayType.Monthly, payType);
		}

		[Fact]
		void trigger_save_component_not_in_snapshot() {
			var world = new World();
			int e = world.Create();
			world.Add(e, new Country("Russian_Empire"));
			world.Add(e, new TriggerSave());

			var snapshot = Snapshot(world);

			// TriggerSave must not appear in any entity snapshot
			foreach (var es in snapshot.Entities) {
				Assert.DoesNotContain(typeof(TriggerSave).FullName, es.Components.Keys);
			}
		}

		[Fact]
		void load_clears_previous_world_state() {
			var original = BuildWorld();
			var snapshot = Snapshot(original);

			var targetWorld = new World();
			// Add an extra entity not in the snapshot
			int extra = targetWorld.Create();
			targetWorld.Add(extra, new Country("EXTRA_COUNTRY"));

			Restore(snapshot, targetWorld);

			// Extra entity should be gone
			int[] req = { TypeId<Country>.Value };
			foreach (var arch in targetWorld.GetMatchingArchetypes(req, null)) {
				for (int i = 0; i < arch.Count; i++) {
					Assert.NotEqual("EXTRA_COUNTRY", arch.GetColumn<Country>()[i].CountryId);
				}
			}
		}

		[Fact]
		void snapshot_header_contains_org_id_and_date() {
			var world = BuildWorld();
			var snapshot = Snapshot(world);

			Assert.Equal("Illuminati", snapshot.Header.OrganizationId);
			Assert.Equal(new DateTime(1882, 6, 15), snapshot.Header.GameDate);
			Assert.StartsWith("Illuminati_1882-06-15", snapshot.Header.SaveName);
		}

		[Fact]
		void round_trip_preserves_character_name_part_keys() {
			var world = new World();
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = "great_britain_ruler_1",
				CountryId = "Great_Britain",
				RoleId = "ruler",
				NamePartKeys = new[] { "character.name.british", "character.name.char_i" }
			});

			var snapshot = Snapshot(world);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<Character>.Value };
			string[]? restoredKeys = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					restoredKeys = arch.GetColumn<Character>()[0].NamePartKeys;
					break;
				}
			}

			Assert.NotNull(restoredKeys);
			Assert.Equal(new[] { "character.name.british", "character.name.char_i" }, restoredKeys);
		}

		[Fact]
		void round_trip_preserves_empty_name_part_keys() {
			var world = new World();
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = "test_ruler",
				CountryId = "Test",
				RoleId = "ruler",
				NamePartKeys = System.Array.Empty<string>()
			});

			var snapshot = Snapshot(world);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<Character>.Value };
			string[]? restoredKeys = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					restoredKeys = arch.GetColumn<Character>()[0].NamePartKeys;
					break;
				}
			}

			Assert.NotNull(restoredKeys);
			Assert.Empty(restoredKeys);
		}

		[Fact]
		void round_trip_preserves_reassigned_province_ownership() {
			var world = new World();
			int provinceEntity = world.Create();
			world.Add(provinceEntity, new ProvinceOwnership { ProvinceId = "prov_a", OwnerId = "France" });

			ref ProvinceOwnership ownership = ref world.Get<ProvinceOwnership>(provinceEntity);
			ownership.OwnerId = "Great_Britain";

			var snapshot = Snapshot(world);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<ProvinceOwnership>.Value };
			string? restoredOwnerId = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					restoredOwnerId = arch.GetColumn<ProvinceOwnership>()[0].OwnerId;
					break;
				}
			}

			Assert.Equal("Great_Britain", restoredOwnerId);
		}

		[Fact]
		void round_trip_preserves_per_org_discovery() {
			var world = new World();
			int e1 = world.Create();
			world.Add(e1, new DiscoveredCountry { OrgId = "OrgA", CountryId = "France" });
			int e2 = world.Create();
			world.Add(e2, new DiscoveredCountry { OrgId = "OrgA", CountryId = "Prussia" });
			int e3 = world.Create();
			world.Add(e3, new DiscoveredCountry { OrgId = "OrgB", CountryId = "Austria" });

			var snapshot = Snapshot(world);
			var restored = new World();
			Restore(snapshot, restored);

			var byOrg = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>();
			int[] req = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (!byOrg.TryGetValue(dcs[i].OrgId, out var set)) {
						set = new System.Collections.Generic.HashSet<string>();
						byOrg[dcs[i].OrgId] = set;
					}
					set.Add(dcs[i].CountryId);
				}
			}

			Assert.Equal(new System.Collections.Generic.HashSet<string> { "France", "Prussia" }, byOrg["OrgA"]);
			Assert.Equal(new System.Collections.Generic.HashSet<string> { "Austria" }, byOrg["OrgB"]);
		}

		[Fact]
		void round_trip_preserves_grown_province_population_and_continues_compounding() {
			var world = new World();
			int provinceEntity = world.Create();
			world.Add(provinceEntity, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(provinceEntity, new Resource {
				ResourceId = ResourceDefinitions.Population,
				Value = 1000.0
			});

			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner("prov_a", OwnerType.Province));
			world.Add(effectEntity, new ResourceLink(ResourceDefinitions.Population));
			world.Add(effectEntity, new ResourceEffect {
				EffectId = "population_growth_prov_a",
				PayType = PayType.Monthly
			});
			world.Add(effectEntity, new ResourceCollector { CollectorId = PopulationGrowthCollector.Id });

			var registry = new ResourceCollectorRegistry();
			registry.Register(PopulationGrowthCollector.Id, new PopulationGrowthCollector(0.075));
			var order = new[] { ResourceDefinitions.Population };

			DateTime jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
			DateTime feb1 = new DateTime(1880, 2, 1, 0, 0, 0);
			ResourceSystem.Update(world, jan31, feb1, registry, order);
			double grownValue = world.Get<Resource>(provinceEntity).Value;
			Assert.Equal(1000.75, grownValue, 6);

			var snapshot = Snapshot(world);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			int restoredEntity = -1;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == "prov_a" && resources[i].ResourceId == ResourceDefinitions.Population) {
						restoredEntity = arch.Entities[i];
					}
				}
			}

			Assert.True(restoredEntity >= 0);
			Assert.Equal(grownValue, restored.Get<Resource>(restoredEntity).Value, 6);

			DateTime feb28 = new DateTime(1880, 2, 28, 23, 0, 0);
			DateTime mar1 = new DateTime(1880, 3, 1, 0, 0, 0);
			ResourceSystem.Update(restored, feb28, mar1, registry, order);
			Assert.Equal(grownValue * 1.00075, restored.Get<Resource>(restoredEntity).Value, 6);
		}

		[Fact]
		void round_trip_preserves_bot_action_log_entries_and_order() {
			var world = new World();
			int botActionLogEntity = world.Create();
			var entries = new[] {
				"1882-06-15 | Illuminati | DiscoverAndControl/spread_rumors -> France",
				"1882-06-16 | Illuminati | DiscoverAndControl/spend_gold",
				"1882-06-17 | Masons | DiscoverAndControl/spread_rumors -> Prussia"
			};
			world.Add(botActionLogEntity, new BotActionLog { Entries = entries });

			var snapshot = Snapshot(world);
			var restored = new World();
			Restore(snapshot, restored);

			int[] req = { TypeId<BotActionLog>.Value };
			string[]? restoredEntries = null;
			foreach (var arch in restored.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					restoredEntries = arch.GetColumn<BotActionLog>()[0].Entries;
					break;
				}
			}

			Assert.Equal(entries, restoredEntries);
		}
	}
}
