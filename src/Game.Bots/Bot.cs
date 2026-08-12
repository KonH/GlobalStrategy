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
		DateTime? _pendingAcquisitionSinceDate;

		// If a country-card draw offer can't be resolved (e.g. the hand stays full and
		// ReceiveCardSystem keeps rejecting it) for this many calendar days in a row, stop
		// letting it block the daily decision loop below - fall through to feature.Tick()
		// anyway so the bot doesn't freeze forever. TryAcquireCountryCard keeps retrying the
		// stuck offer on every subsequent tick regardless; a feature play may also free hand
		// capacity and let a retry finally succeed.
		const int AcquisitionStallDayLimit = 3;

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
			DateTime currentDate = BotObservation.ReadCurrentDate(world);

			// A full observation rebuild is a cards x countries ActionPlayability scan -
			// expensive, and only two things can ever make it worthwhile: an unresolved
			// country-card acquisition step (draw/receive), or today's strategic decision not
			// having been made yet (feature.Tick() below only ever runs once per calendar day,
			// gated by _lastActedDate). Once today's decision is made and there's no acquisition
			// work pending, nothing can change again until the day rolls over or a new draw
			// becomes available - skip the rebuild entirely rather than recomputing it and
			// immediately discarding the result every frame.
			bool alreadyActedToday = _lastActedDate.HasValue && currentDate.Date == _lastActedDate.Value.Date;
			if (alreadyActedToday && !HasPendingAcquisitionWork(world, actionConfig)) {
				return;
			}

			_sink.BeginDecisionPhase();
			var observation = BotObservation.Build(
				world, actionConfig, OrgId, _resources, _relations, _effectConfig, _hqCountryByOrgId, _maxControlPool);
			if (TryAcquireCountryCard(observation)) {
				_pendingAcquisitionSinceDate ??= currentDate.Date;
				bool acquisitionStalled =
					(currentDate.Date - _pendingAcquisitionSinceDate.Value).Days >= AcquisitionStallDayLimit;
				if (!acquisitionStalled) {
					return;
				}
				// Stalled - fall through to the strategic tick below instead of returning.
			} else {
				_pendingAcquisitionSinceDate = null;
			}

			if (alreadyActedToday) {
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

		bool HasPendingAcquisitionWork(IReadOnlyWorld world, ActionConfig actionConfig) {
			return CountryCardDrawQuery.TryGetStatus(world, actionConfig, OrgId, out CountryCardDrawStatus status)
				&& (status.CanStartDraw || status.HasCoherentPendingDraw);
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
