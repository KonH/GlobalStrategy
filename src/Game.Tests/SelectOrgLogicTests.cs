using System.Collections.Generic;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SelectOrgLogicTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static SelectOrgLogic BuildLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain" },
					new CountryEntry { CountryId = "France", DisplayName = "France" }
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
			var resourceConfig = new ResourceConfig();
			return new SelectOrgLogic(
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				resourceConfig);
		}

		[Fact]
		void clicking_hq_country_sets_org_state() {
			var logic = BuildLogic();
			logic.Commands.Push(new SelectCountryCommand("Great_Britain"));
			logic.Update();

			Assert.True(logic.VisualState.SelectedOrganization.IsValid);
			Assert.Equal("Illuminati", logic.VisualState.SelectedOrganization.OrgId);
			Assert.Equal("Illuminati", logic.VisualState.SelectedOrganization.DisplayName);
			Assert.Equal(1000.0, logic.VisualState.SelectedOrganization.InitialGold);
		}

		[Fact]
		void clicking_non_hq_country_does_not_set_org_state() {
			var logic = BuildLogic();
			logic.Commands.Push(new SelectCountryCommand("France"));
			logic.Update();

			Assert.True(logic.VisualState.SelectedCountry.IsValid);
			Assert.Equal("France", logic.VisualState.SelectedCountry.CountryId);
			Assert.False(logic.VisualState.SelectedOrganization.IsValid);
		}

		[Fact]
		void hq_country_ids_contains_only_hq_countries() {
			var logic = BuildLogic();
			Assert.Contains("Great_Britain", logic.HqCountryIds);
			Assert.DoesNotContain("France", logic.HqCountryIds);
		}
	}
}
