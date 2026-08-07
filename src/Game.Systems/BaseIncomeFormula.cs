using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public readonly struct BaseIncomeBreakdown {
		public double FlatBase { get; }
		public double Population { get; }
		public double PopulationContribution { get; }
		public int ProvinceCount { get; }
		public double ProvinceContribution { get; }
		public double AdvisorSkill { get; }
		public double AdvisorContribution { get; }
		public double Total { get; }

		public BaseIncomeBreakdown(
			double flatBase,
			double population, double populationContribution,
			int provinceCount, double provinceContribution,
			double advisorSkill, double advisorContribution,
			double total) {
			FlatBase = flatBase;
			Population = population;
			PopulationContribution = populationContribution;
			ProvinceCount = provinceCount;
			ProvinceContribution = provinceContribution;
			AdvisorSkill = advisorSkill;
			AdvisorContribution = advisorContribution;
			Total = total;
		}
	}

	public static class BaseIncomeFormula {
		public const string EffectId = "base_income";

		public static BaseIncomeBreakdown ComputeFromInputs(
			double population, int provinceCount, double advisorSkill, BaseIncomeSettings settings) {
			double populationContribution = settings.PopulationWeight
				* (population / 1_000_000.0)
				* settings.PopulationGoldPerMillion;
			double provinceContribution = settings.ProvinceWeight
				* provinceCount
				* settings.ProvinceGoldPerProvince;
			double advisorContribution = settings.AdvisorWeight * advisorSkill;
			double total = settings.FlatBase + populationContribution + provinceContribution + advisorContribution;
			return new BaseIncomeBreakdown(
				settings.FlatBase,
				population, populationContribution,
				provinceCount, provinceContribution,
				advisorSkill, advisorContribution,
				total);
		}

		public static BaseIncomeBreakdown Compute(IReadOnlyWorld world, string countryId, BaseIncomeSettings settings) {
			double population = ResourceQuery.GetValue(world, countryId, ResourceDefinitions.CountryPopulation);
			int provinceCount = CountOwnedProvinces(world, countryId);
			double advisorSkill = WartimeSkillQuery.GetSkill(world, countryId, "economic_advisor", "stinginess");
			return ComputeFromInputs(population, provinceCount, advisorSkill, settings);
		}

		public static double ComputeFromConfig(
			ProvinceConfig provinceConfig,
			CharacterConfig? characterConfig,
			string countryId,
			BaseIncomeSettings settings) {
			double population = 0;
			int provinceCount = 0;
			foreach (var province in provinceConfig.FindByCountryId(countryId)) {
				population += province.Population;
				provinceCount++;
			}
			double advisorSkill = EstimateAdvisorSkill(characterConfig, countryId);
			return ComputeFromInputs(population, provinceCount, advisorSkill, settings).Total;
		}

		static int CountOwnedProvinces(IReadOnlyWorld world, string countryId) {
			var byOwner = ProvinceOwnershipSystem.GetProvincesByOwner(world);
			return byOwner.TryGetValue(countryId, out var provinces) ? provinces.Count : 0;
		}

		static double EstimateAdvisorSkill(CharacterConfig? characterConfig, string countryId) {
			if (characterConfig == null) {
				return 0;
			}
			var pool = characterConfig.FindPool(countryId);
			if (pool == null || !pool.Slots.TryGetValue("economic_advisor", out var candidates) || candidates.Count == 0) {
				return 0;
			}
			double sum = 0;
			int counted = 0;
			foreach (var candidate in candidates) {
				if (!candidate.Skills.TryGetValue("stinginess", out var settings)) {
					continue;
				}
				sum += (settings.MinValue + settings.MaxValue) / 2.0;
				counted++;
			}
			return counted == 0 ? 0 : sum / counted;
		}
	}
}
