using System.Collections.Generic;
using ECS;

namespace GS.Game.Systems {
	public sealed class DurabilityCollector : IResourceCollector {
		public const string Id = "durability_formula";

		readonly IReadOnlyDictionary<string, int> _baseDurabilityByCountryId;

		public DurabilityCollector(IReadOnlyDictionary<string, int> baseDurabilityByCountryId) {
			_baseDurabilityByCountryId = baseDurabilityByCountryId;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			int baseDurability = _baseDurabilityByCountryId.TryGetValue(ownerId, out int b) ? b : 40;
			double rulerStinginess = WartimeSkillQuery.GetSkill(world, ownerId, "ruler", "stinginess");
			double economicStinginess = WartimeSkillQuery.GetSkill(world, ownerId, "economic_advisor", "stinginess");
			double target = baseDurability + rulerStinginess + economicStinginess;
			return target - currentValue;
		}
	}
}
