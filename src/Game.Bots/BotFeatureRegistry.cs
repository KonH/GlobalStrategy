using System;
using System.Collections.Generic;

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

		public static BotFeatureRegistry CreateDefault(int maxControlPool) {
			var registry = new BotFeatureRegistry();
			registry.Register(BaselineCardPlayFeature.Id, parameters => new BaselineCardPlayFeature(parameters));
			registry.Register(DiscoverAndControlFeature.Id, parameters => new DiscoverAndControlFeature(parameters, maxControlPool));
			return registry;
		}
	}
}
