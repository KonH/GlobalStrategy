using System;
using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Game.Bots {
	public sealed class BotFeatureRegistry {
		readonly Dictionary<string, Func<IReadOnlyDictionary<string, double>, IBotFeature>> _factories = new();

		public void Register(string featureId, Func<IReadOnlyDictionary<string, double>, IBotFeature> factory) {
			_factories[featureId] = factory;
		}

		public IBotFeature Create(string featureId, IReadOnlyDictionary<string, double> parameters) {
			if (!_factories.TryGetValue(featureId, out var factory)) {
				throw new InvalidOperationException($"Unknown bot feature id: {featureId}");
			}
			return factory(parameters);
		}

		public bool IsRegistered(string featureId) => _factories.ContainsKey(featureId);

		public static BotFeatureRegistry CreateDefault(int maxControlPool, EffectConfig? effectConfig = null) {
			var registry = new BotFeatureRegistry();
			double sellArmsBonus = WarProsecuteFeature.ResolveSellArmsDamageBonusPercent(effectConfig);
			registry.Register(BaselineCardPlayFeature.Id, parameters => new BaselineCardPlayFeature(parameters));
			registry.Register(ControlFeature.Id, parameters => new ControlFeature(parameters, maxControlPool));
			registry.Register(WarDeclareFeature.Id, parameters => new WarDeclareFeature(parameters, maxControlPool));
			registry.Register(WarUnlockFeature.Id, parameters => new WarUnlockFeature(parameters, maxControlPool));
			registry.Register(WarProsecuteFeature.Id, parameters => new WarProsecuteFeature(parameters, maxControlPool, sellArmsBonus));
			return registry;
		}
	}
}
