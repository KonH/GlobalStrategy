using System.Collections.Generic;
using ECS;

namespace GS.Game.Systems {
	public sealed class DamageCollector : IResourceCollector {
		public const string Id = "damage_formula";

		readonly IReadOnlyDictionary<string, int> _baseDamageByCountryId;

		public DamageCollector(IReadOnlyDictionary<string, int> baseDamageByCountryId) {
			_baseDamageByCountryId = baseDamageByCountryId;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			int baseDamage = _baseDamageByCountryId.TryGetValue(ownerId, out int b) ? b : 40;
			double rulerPower = WartimeSkillQuery.GetSkill(world, ownerId, "ruler", "power");
			double militaryPower = WartimeSkillQuery.GetSkill(world, ownerId, "military_advisor", "power");
			double target = baseDamage + rulerPower + militaryPower;
			return target - currentValue;
		}
	}
}
