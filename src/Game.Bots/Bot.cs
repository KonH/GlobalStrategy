using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Bots {
	public sealed class Bot {
		readonly IReadOnlyList<IBotFeature> _features;
		readonly Random _rng;
		readonly BotCommandSink _sink;

		public string OrgId { get; }

		public Bot(string orgId, IReadOnlyList<IBotFeature> features, Random rng, BotCommandSink sink) {
			OrgId = orgId;
			_features = features;
			_rng = rng;
			_sink = sink;
		}

		public void ExecuteDecisionTick(IReadOnlyWorld world, ActionConfig actionConfig) {
			_sink.BeginDecisionPhase();
			var observation = BotObservation.Build(world, actionConfig, OrgId);
			foreach (var feature in _features) {
				try {
					feature.Tick(observation, _sink, _rng);
				} catch (Exception ex) {
					throw new BotFeatureException(OrgId, feature.FeatureId, ex);
				}
			}
		}
	}

	public sealed class BotFeatureException : Exception {
		public string OrgId { get; }
		public string FeatureId { get; }

		public BotFeatureException(string orgId, string featureId, Exception inner)
			: base($"Bot feature '{featureId}' threw for org '{orgId}': {inner.Message}", inner) {
			OrgId = orgId;
			FeatureId = featureId;
		}
	}
}
