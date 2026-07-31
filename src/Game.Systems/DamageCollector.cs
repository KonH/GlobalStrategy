using System.Collections.Generic;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class DamageCollector : IResourceCollector {
		public const string Id = "damage_formula";

		readonly IReadOnlyDictionary<string, CountryCombatBases> _basesByCountryId;

		public DamageCollector(IReadOnlyDictionary<string, CountryCombatBases> basesByCountryId) {
			_basesByCountryId = basesByCountryId;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			int baseDamage = _basesByCountryId.TryGetValue(ownerId, out var bases) ? bases.BaseDamage : 40;
			double rulerPower = WartimeSkillQuery.GetSkill(world, ownerId, "ruler", "power");
			double militaryPower = WartimeSkillQuery.GetSkill(world, ownerId, "military_advisor", "power");
			double bonusPercent = ResourceQuery.GetValue(world, ownerId, ResourceDefinitions.TroopsDamageBonusPercent);
			double revengeBonusPercent = RevengeWarBonusQuery.GetBonusPercent(world, ownerId, "damage");
			double target = (baseDamage + rulerPower + militaryPower)
				* (1.0 + bonusPercent / 100.0)
				* (1.0 + revengeBonusPercent / 100.0);
			return target - currentValue;
		}
	}
}
