using System;
using System.Linq;
using ECS;
using ECS.Extensions;
using GS.Game.Components;
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
	}
}
