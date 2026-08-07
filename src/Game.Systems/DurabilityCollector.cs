using System.Collections.Generic;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class DurabilityCollector : IResourceCollector {
		public const string Id = "durability_formula";

		readonly IReadOnlyDictionary<string, CountryCombatBases> _basesByCountryId;

		public DurabilityCollector(IReadOnlyDictionary<string, CountryCombatBases> basesByCountryId) {
			_basesByCountryId = basesByCountryId;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world, ResourceQuery resources) {
			int baseDurability = _basesByCountryId.TryGetValue(ownerId, out var bases) ? bases.BaseDurability : 40;
			double rulerStinginess = WartimeSkillQuery.GetSkill(world, ownerId, "ruler", "stinginess", resources);
			double economicStinginess = WartimeSkillQuery.GetSkill(world, ownerId, "economic_advisor", "stinginess", resources);
			double target = (baseDurability + rulerStinginess + economicStinginess)
				* (1.0 + RevengeWarBonusQuery.GetBonusPercent(world, ownerId, "durability") / 100.0);
			return target - currentValue;
		}
	}
}
