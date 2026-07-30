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
			double target = baseDamage + rulerPower + militaryPower;
			return target - currentValue;
		}
	}
}
