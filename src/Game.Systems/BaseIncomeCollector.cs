using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class BaseIncomeCollector : IResourceCollector {
		public const string Id = "base_income_formula";

		readonly BaseIncomeSettings _settings;

		public BaseIncomeCollector(BaseIncomeSettings settings) {
			_settings = settings;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world, ResourceQuery resources) {
			return BaseIncomeFormula.Compute(world, ownerId, _settings, resources).Total;
		}
	}
}
