using System.Collections.Generic;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SelectOrgLogicTests {
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
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
			var gameSettings = new GameSettings();
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "gb_1", CountryId = "Great_Britain", Population = 2_000_000 },
					new ProvinceEntry { ProvinceId = "gb_2", CountryId = "Great_Britain", Population = 3_000_000 }
				}
			};
			var characterConfig = new CharacterConfig {
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "Great_Britain",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["economic_advisor"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "gb_eco_1",
									Skills = new Dictionary<string, SkillSettings> {
										["stinginess"] = new SkillSettings { MinValue = 40, MaxValue = 60 }
									}
								}
							}
						}
					}
				}
			};
			return new SelectOrgLogic(
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				resourceConfig,
				new StaticConfig<GameSettings>(gameSettings),
				provinceConfig,
				characterConfig);
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

		[Fact]
		void win_condition_hint_is_built_once_from_game_settings() {
			var logic = BuildLogic();
			Assert.True(logic.VisualState.WinConditionHint.IsAvailable);
			Assert.True(logic.VisualState.WinConditionHint.IsAlternativeGroup);
			Assert.Collection(logic.VisualState.WinConditionHint.Rows,
				row => Assert.Equal(WinConditionHintKind.ScoreGoal, row.Kind),
				row => Assert.Equal(WinConditionHintKind.LastOrgStanding, row.Kind));
		}

		[Fact]
		void estimated_income_uses_hq_country_population_provinces_and_advisor() {
			var logic = BuildLogic();
			// flat 10 + pop 0.5*(5M/1M)*0.1=0.25 + prov 0.3*2*0.05=0.03 + adv 0.2*50=10 => 20.28
			// base control default 10% => 2.028
			Assert.Equal(2.028, logic.ComputeBaseControlIncome("Illuminati"), 6);
		}
	}
}
