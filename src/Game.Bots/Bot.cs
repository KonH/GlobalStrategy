using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Bots {
	public sealed class Bot {
		readonly IReadOnlyList<IBotFeature> _features;
		readonly Random _rng;
		readonly BotCommandSink _sink;
		readonly EffectConfig _effectConfig;
		readonly ResourceQuery _resources;
		readonly CountryRelations _relations;
		readonly IReadOnlyDictionary<string, string>? _hqCountryByOrgId;
		readonly int _maxControlPool;
		DateTime? _lastActedDate;

		public string OrgId { get; }
		public string CurrentFeatureId { get; private set; } = "";

		public Bot(
			string orgId,
			IReadOnlyList<IBotFeature> features,
			Random rng,
			BotCommandSink sink,
			ResourceQuery resources,
			CountryRelations relations,
			EffectConfig? effectConfig = null,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			int maxControlPool = 100) {
			OrgId = orgId;
			_features = features;
			_rng = rng;
			_sink = sink;
			_resources = resources;
			_relations = relations;
			_effectConfig = effectConfig ?? new EffectConfig();
			_hqCountryByOrgId = hqCountryByOrgId;
			_maxControlPool = maxControlPool;
		}

		public void ExecuteDecisionTick(IReadOnlyWorld world, ActionConfig actionConfig) {
			_sink.BeginDecisionPhase();
			var observation = BotObservation.Build(
				world, actionConfig, OrgId, _resources, _relations, _effectConfig, _hqCountryByOrgId, _maxControlPool);
			if (TryAcquireCountryCard(observation)) {
				return;
			}

			DateTime currentDate = observation.CurrentDate;
			if (_lastActedDate.HasValue && currentDate.Date == _lastActedDate.Value.Date) {
				return;
			}
			_lastActedDate = currentDate;

			foreach (var feature in _features) {
				CurrentFeatureId = feature.FeatureId;
				try {
					feature.Tick(observation, _sink, _rng);
				} catch (Exception ex) {
					throw new BotFeatureException(OrgId, feature.FeatureId, ex);
				} finally {
					CurrentFeatureId = "";
				}
			}
		}

		bool TryAcquireCountryCard(IBotObservation observation) {
			if (observation.CountryCardDrawChoices.Count > 0) {
				BotCardDrawChoiceView selected = observation.CountryCardDrawChoices[0];
				int selectedPriority = GetChoicePriority(selected);
				for (int i = 1; i < observation.CountryCardDrawChoices.Count; i++) {
					BotCardDrawChoiceView candidate = observation.CountryCardDrawChoices[i];
					int candidatePriority = GetChoicePriority(candidate);
					if (candidatePriority < selectedPriority
						|| candidatePriority == selectedPriority && candidate.ChoiceIndex < selected.ChoiceIndex) {
						selected = candidate;
						selectedPriority = candidatePriority;
					}
				}
				_sink.ReceiveCountryCard(selected.ChoiceIndex);
				return true;
			}
			if (observation.CanStartCountryCardDraw) {
				_sink.DrawCountryCards();
				return true;
			}
			return false;
		}

		static int GetChoicePriority(BotCardDrawChoiceView choice) {
			if (choice.IsControlUsable) {
				return 0;
			}
			if (choice.RaisesControl) {
				return 1;
			}
			if (choice.IsPlayable) {
				return 2;
			}
			return 3;
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
