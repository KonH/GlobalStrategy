using System.Collections.Generic;
using GS.Game.Configs;
using GS.Game.Loader;
using Xunit;

using GS.Game.Systems;

namespace GS.Game.Tests {
	public class LoaderCountryPreservationTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		[Fact]
		void preserves_is_available_and_initial_resources_from_existing_entry() {
			var rebuilt = new List<CountryEntry> {
				new CountryEntry { CountryId = "France", DisplayName = "France" }
			};
			var existing = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "France",
						DisplayName = "France",
						IsAvailable = true,
						InitialResources = new List<CountryResourceInit> {
							new CountryResourceInit { ResourceId = "Gold", Value = 100.0 }
						}
					}
				}
			};

			Program.ApplyPreservedFields(rebuilt, existing);

			Assert.True(rebuilt[0].IsAvailable);
			Assert.Single(rebuilt[0].InitialResources);
			Assert.Equal("Gold", rebuilt[0].InitialResources[0].ResourceId);
		}

		[Fact]
		void preserves_historical_friends_and_rivals_from_existing_entry() {
			var rebuilt = new List<CountryEntry> {
				new CountryEntry { CountryId = "France", DisplayName = "France" }
			};
			var existing = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "France",
						DisplayName = "France",
						HistoricalFriends = new List<string> { "Belgium" },
						HistoricalRivals = new List<string> { "Germany" }
					}
				}
			};

			Program.ApplyPreservedFields(rebuilt, existing);

			Assert.Single(rebuilt[0].HistoricalFriends);
			Assert.Equal("Belgium", rebuilt[0].HistoricalFriends[0]);
			Assert.Single(rebuilt[0].HistoricalRivals);
			Assert.Equal("Germany", rebuilt[0].HistoricalRivals[0]);
		}

		[Fact]
		void preserves_base_damage_and_base_durability_from_existing_entry() {
			var rebuilt = new List<CountryEntry> {
				new CountryEntry { CountryId = "France", DisplayName = "France" }
			};
			var existing = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "France",
						DisplayName = "France",
						BaseDamage = 85,
						BaseDurability = 82
					}
				}
			};

			Program.ApplyPreservedFields(rebuilt, existing);

			Assert.Equal(85, rebuilt[0].BaseDamage);
			Assert.Equal(82, rebuilt[0].BaseDurability);
		}

		[Fact]
		void leaves_defaults_when_no_matching_existing_entry() {
			var rebuilt = new List<CountryEntry> {
				new CountryEntry { CountryId = "Germany", DisplayName = "Germany" }
			};
			var existing = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true }
				}
			};

			Program.ApplyPreservedFields(rebuilt, existing);

			Assert.False(rebuilt[0].IsAvailable);
			Assert.Empty(rebuilt[0].InitialResources);
			Assert.Empty(rebuilt[0].HistoricalFriends);
			Assert.Empty(rebuilt[0].HistoricalRivals);
			Assert.Equal(40, rebuilt[0].BaseDamage);
			Assert.Equal(40, rebuilt[0].BaseDurability);
		}

		[Fact]
		void leaves_defaults_when_existing_config_is_null() {
			var rebuilt = new List<CountryEntry> {
				new CountryEntry { CountryId = "France", DisplayName = "France" }
			};

			Program.ApplyPreservedFields(rebuilt, null);

			Assert.False(rebuilt[0].IsAvailable);
			Assert.Empty(rebuilt[0].InitialResources);
			Assert.Equal(40, rebuilt[0].BaseDamage);
			Assert.Equal(40, rebuilt[0].BaseDurability);
		}
	}
}
