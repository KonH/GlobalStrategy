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
	public class ProvinceOwnershipTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static GameLogic BuildLogic() {
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
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = "Great_Britain" },
					new ProvinceEntry { ProvinceId = "prov_b", CountryId = "France" }
				}
			};

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialPlayerCountryId: "Great_Britain",
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

		[Fact]
		void seed_creates_one_ownership_entity_per_province_from_config() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal(2, CountEntities<ProvinceOwnership>(logic.World));
			Assert.Equal("Great_Britain", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_a"));
			Assert.Equal("France", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_b"));
		}

		[Fact]
		void change_owner_updates_owner_field() {
			var logic = BuildLogic();
			logic.Update(0f);

			var (changed, oldOwnerId) = ProvinceOwnershipSystem.ChangeOwner(logic.World, "prov_b", "Great_Britain");

			Assert.True(changed);
			Assert.Equal("France", oldOwnerId);
			Assert.Equal("Great_Britain", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_b"));
		}

		[Fact]
		void change_owner_to_same_owner_is_noop() {
			var logic = BuildLogic();
			logic.Update(0f);

			var (changed, oldOwnerId) = ProvinceOwnershipSystem.ChangeOwner(logic.World, "prov_a", "Great_Britain");

			Assert.False(changed);
			Assert.Equal("", oldOwnerId);
			Assert.Equal("Great_Britain", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_a"));
		}

		[Fact]
		void change_owner_unknown_province_is_noop() {
			var logic = BuildLogic();
			logic.Update(0f);

			var (changed, oldOwnerId) = ProvinceOwnershipSystem.ChangeOwner(logic.World, "prov_unknown", "Great_Britain");

			Assert.False(changed);
			Assert.Equal("", oldOwnerId);
		}

		[Fact]
		void get_provinces_by_owner_reflects_reassignment() {
			var logic = BuildLogic();
			logic.Update(0f);

			ProvinceOwnershipSystem.ChangeOwner(logic.World, "prov_b", "Great_Britain");
			var byOwner = ProvinceOwnershipSystem.GetProvincesByOwner(logic.World);

			Assert.True(byOwner.TryGetValue("Great_Britain", out var gbProvinces));
			Assert.Contains("prov_a", gbProvinces);
			Assert.Contains("prov_b", gbProvinces);
			Assert.False(byOwner.TryGetValue("France", out var franceProvinces) && franceProvinces.Contains("prov_b"));
		}

		[Fact]
		void get_owner_returns_current_runtime_owner() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.Equal("France", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_b"));
			ProvinceOwnershipSystem.ChangeOwner(logic.World, "prov_b", "Great_Britain");
			Assert.Equal("Great_Britain", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_b"));
		}

		[Fact]
		void debug_change_province_owner_command_updates_owner_through_pipeline() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugChangeProvinceOwnerCommand { ProvinceId = "prov_b", NewOwnerId = "Great_Britain" });
			logic.Update(0f);

			Assert.Equal("Great_Britain", ProvinceOwnershipSystem.GetOwner(logic.World, "prov_b"));
		}
	}
}
