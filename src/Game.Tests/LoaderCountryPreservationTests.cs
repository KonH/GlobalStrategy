using System.Collections.Generic;
using GS.Game.Configs;
using GS.Game.Loader;
using Xunit;

namespace GS.Game.Tests {
	public class LoaderCountryPreservationTests {
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
		}

		[Fact]
		void leaves_defaults_when_existing_config_is_null() {
			var rebuilt = new List<CountryEntry> {
				new CountryEntry { CountryId = "France", DisplayName = "France" }
			};

			Program.ApplyPreservedFields(rebuilt, null);

			Assert.False(rebuilt[0].IsAvailable);
			Assert.Empty(rebuilt[0].InitialResources);
		}
	}
}
