using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CountryRelationSeedingTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static GameLogic BuildLogic(CountryConfig countryConfig) {
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
			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(new ResourceConfig { Resources = new List<ResourceDefinition>() }),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				province: new StaticConfig<ProvinceConfig>(new ProvinceConfig()));
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
		void one_sided_historical_relation_is_seeded_and_queryable_from_both_sides() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true,
						HistoricalFriends = new List<string> { "France" }
					},
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true }
				}
			};
			var logic = BuildLogic(countryConfig);

			logic.Update(0f);

			Assert.Equal(1, CountEntities<CountryRelation>(logic.World));
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(logic.World, "Great_Britain", "France"));
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(logic.World, "France", "Great_Britain"));
		}

		[Fact]
		void historical_entry_naming_unavailable_country_seeds_nothing_and_does_not_affect_others() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true,
						HistoricalFriends = new List<string> { "Unavailable_Country", "France" }
					},
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true },
					new CountryEntry { CountryId = "Unavailable_Country", DisplayName = "Unavailable", IsAvailable = false }
				}
			};
			var logic = BuildLogic(countryConfig);

			logic.Update(0f);

			Assert.Equal(1, CountEntities<CountryRelation>(logic.World));
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(logic.World, "Great_Britain", "France"));
			Assert.Null(CountryRelations.GetRelation(logic.World, "Great_Britain", "Unavailable_Country"));
		}

		[Fact]
		void conflicting_declaration_resolves_deterministically_to_first_declared_kind() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true,
						HistoricalFriends = new List<string> { "France" }
					},
					new CountryEntry {
						CountryId = "France", DisplayName = "France", IsAvailable = true,
						HistoricalRivals = new List<string> { "Great_Britain" }
					}
				}
			};
			var logic = BuildLogic(countryConfig);

			logic.Update(0f);

			Assert.Equal(1, CountEntities<CountryRelation>(logic.World));
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(logic.World, "Great_Britain", "France"));
		}
	}
}
