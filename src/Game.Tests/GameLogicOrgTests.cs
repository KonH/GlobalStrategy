using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class GameLogicOrgTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static GameLogic BuildLogic(string orgId = "Illuminati") {
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
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition>()
			};
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialPlayerCountryId: "Great_Britain",
				initialOrganizationId: orgId
			);
			return new GameLogic(ctx);
		}

		[Fact]
		void org_entity_exists_with_correct_id() {
			var logic = BuildLogic();
			logic.Update(0f);
			var world = logic.World;

			string? foundOrgId = null;
			int[] req = { TypeId<Organization>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					foundOrgId = arch.GetColumn<Organization>()[0].OrganizationId;
					break;
				}
			}

			Assert.Equal("Illuminati", foundOrgId);
		}

		[Fact]
		void org_gold_resource_entity_has_correct_owner_and_value() {
			var logic = BuildLogic();
			logic.Update(0f);
			var world = logic.World;

			double? goldValue = null;
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == "Illuminati" && resources[i].ResourceId == "gold") {
						goldValue = resources[i].Value;
					}
				}
			}

			Assert.Equal(1000.0, goldValue);
		}
	}
}
