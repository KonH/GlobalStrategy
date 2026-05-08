using System.Collections.Generic;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class OrganizationConfigTests {
		static OrganizationConfig BuildConfig() {
			return new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					},
					new OrganizationEntry {
						OrganizationId = "TestOrg",
						DisplayName = "Test Organization",
						HqCountryId = "France",
						InitialGold = 500.0
					}
				}
			};
		}

		[Fact]
		void find_by_hq_country_returns_correct_entry() {
			var config = BuildConfig();
			var entry = config.FindByHqCountry("Great_Britain");
			Assert.NotNull(entry);
			Assert.Equal("Illuminati", entry!.OrganizationId);
		}

		[Fact]
		void find_by_hq_country_returns_null_for_unknown() {
			var config = BuildConfig();
			Assert.Null(config.FindByHqCountry("Unknown_Country"));
		}

		[Fact]
		void find_by_id_returns_correct_entry() {
			var config = BuildConfig();
			var entry = config.FindById("TestOrg");
			Assert.NotNull(entry);
			Assert.Equal("France", entry!.HqCountryId);
			Assert.Equal(500.0, entry.InitialGold);
		}

		[Fact]
		void find_by_id_returns_null_for_unknown() {
			var config = BuildConfig();
			Assert.Null(config.FindById("NonExistent"));
		}

		[Fact]
		void entry_fields_are_correct() {
			var config = BuildConfig();
			var entry = config.FindById("Illuminati");
			Assert.NotNull(entry);
			Assert.Equal("Illuminati", entry!.DisplayName);
			Assert.Equal("Great_Britain", entry.HqCountryId);
			Assert.Equal(1000.0, entry.InitialGold);
		}
	}
}
