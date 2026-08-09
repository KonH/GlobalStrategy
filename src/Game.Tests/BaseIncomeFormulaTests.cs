using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class BaseIncomeFormulaTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static readonly BaseIncomeSettings Settings = new BaseIncomeSettings();

		[Fact]
		void compute_from_inputs_applies_flat_base_and_weighted_terms() {
			// flat 10
			// pop: 0.5 * (2M/1M) * 0.1 = 0.1
			// provinces: 0.3 * 4 * 0.05 = 0.06
			// advisor: 0.2 * 50 = 10
			var breakdown = BaseIncomeFormula.ComputeFromInputs(2_000_000, 4, 50, Settings);

			Assert.Equal(10.0, breakdown.FlatBase);
			Assert.Equal(0.1, breakdown.PopulationContribution, 6);
			Assert.Equal(0.06, breakdown.ProvinceContribution, 6);
			Assert.Equal(10.0, breakdown.AdvisorContribution);
			Assert.Equal(20.16, breakdown.Total, 6);
		}

		[Fact]
		void compute_from_world_reads_population_provinces_and_advisor() {
			var world = new World();
			AddCountryPopulation(world, "France", 1_000_000);
			AddOwnedProvince(world, "France", "fr_1");
			AddOwnedProvince(world, "France", "fr_2");
			AddCharacterWithSkill(world, "eco_1", "France", "economic_advisor", "stinginess", 40);

			var breakdown = BaseIncomeFormula.Compute(world, "France", Settings, _resources);

			Assert.Equal(10.0, breakdown.FlatBase);
			Assert.Equal(0.05, breakdown.PopulationContribution, 6);
			Assert.Equal(0.03, breakdown.ProvinceContribution, 6);
			Assert.Equal(8.0, breakdown.AdvisorContribution);
			Assert.Equal(18.08, breakdown.Total, 6);
		}

		[Fact]
		void compute_from_config_uses_province_totals_and_advisor_midpoint() {
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "a", CountryId = "France", Population = 1_000_000 },
					new ProvinceEntry { ProvinceId = "b", CountryId = "France", Population = 1_000_000 }
				}
			};
			var characterConfig = new CharacterConfig {
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "France",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["economic_advisor"] = new List<CharacterEntry> {
								new CharacterEntry {
									Skills = new Dictionary<string, SkillSettings> {
										["stinginess"] = new SkillSettings { MinValue = 20, MaxValue = 40 }
									}
								}
							}
						}
					}
				}
			};

			double total = BaseIncomeFormula.ComputeFromConfig(provinceConfig, characterConfig, "France", Settings);

			// flat 10 + pop 0.5*2*0.1=0.1 + prov 0.3*2*0.05=0.03 + adv 0.2*30=6 => 16.13
			Assert.Equal(16.13, total, 6);
		}

		[Fact]
		void collector_returns_total_income_rate() {
			var world = new World();
			AddCountryPopulation(world, "France", 1_000_000);
			AddOwnedProvince(world, "France", "fr_1");
			AddCharacterWithSkill(world, "eco_1", "France", "economic_advisor", "stinginess", 50);
			var collector = new BaseIncomeCollector(Settings);

			double rate = collector.Compute("France", 999.0, world, _resources);

			// flat 10 + pop 0.05 + prov 0.015 + adv 10 = 20.065
			Assert.Equal(20.065, rate, 6);
		}

		static void AddCountryPopulation(World world, string countryId, double population) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(e, new Resource { ResourceId = ResourceDefinitions.CountryPopulation, Value = population });
		}

		static void AddOwnedProvince(World world, string countryId, string provinceId) {
			int e = world.Create();
			world.Add(e, new ProvinceOwnership {
				ProvinceId = provinceId,
				OwnerId = countryId
			});
		}

		static void AddCharacterWithSkill(
			World world, string characterId, string countryId, string roleId, string skillId, double skillValue) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = characterId,
				CountryId = countryId,
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = System.Array.Empty<string>()
			});
			int skillEntity = world.Create();
			world.Add(skillEntity, new ResourceOwner(characterId, OwnerType.Character));
			world.Add(skillEntity, new Resource { ResourceId = skillId, Value = skillValue });
		}
	}
}
