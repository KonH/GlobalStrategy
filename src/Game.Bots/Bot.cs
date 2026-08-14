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
		readonly double _botDecisionIntervalHours;
		readonly double _discardGoldCost;
		DateTime? _lastCycleCompletedAt;
		bool _cycleOpen;
		DateTime? _pendingAcquisitionSince;

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
			int maxControlPool = 100,
			double botDecisionIntervalHours = 4,
			double discardGoldCost = 50) {
			if (botDecisionIntervalHours <= 0) {
				throw new InvalidOperationException(
					$"BotDecisionIntervalHours must be > 0 (got {botDecisionIntervalHours}).");
			}
			OrgId = orgId;
			_features = features;
			_rng = rng;
			_sink = sink;
			_resources = resources;
			_relations = relations;
			_effectConfig = effectConfig ?? new EffectConfig();
			_hqCountryByOrgId = hqCountryByOrgId;
			_maxControlPool = maxControlPool;
			_botDecisionIntervalHours = botDecisionIntervalHours;
			_discardGoldCost = discardGoldCost;
		}

		public void ExecuteDecisionTick(IReadOnlyWorld world, ActionConfig actionConfig) {
			DateTime currentDate = BotObservation.ReadCurrentDate(world);

			if (!_cycleOpen) {
				bool intervalElapsed = !_lastCycleCompletedAt.HasValue
					|| (currentDate - _lastCycleCompletedAt.Value).TotalHours >= _botDecisionIntervalHours;
				if (!intervalElapsed) {
					return;
				}
				_cycleOpen = true;
			}

			_sink.BeginDecisionPhase();
			var observation = BotObservation.Build(
				world, actionConfig, OrgId, _resources, _relations, _effectConfig, _hqCountryByOrgId, _maxControlPool);
			if (TryAcquireCountryCard(observation)) {
				_pendingAcquisitionSince ??= currentDate;
				double stallHours = Math.Max(_botDecisionIntervalHours * 3, 24);
				bool acquisitionStalled =
					(currentDate - _pendingAcquisitionSince.Value).TotalHours >= stallHours;
				if (!acquisitionStalled) {
					return;
				}
				// Stalled — fall through to proposals/discard instead of freezing strategic play.
			} else {
				_pendingAcquisitionSince = null;
			}

			var proposals = new List<BotPlayProposal>();
			foreach (var feature in _features) {
				try {
					feature.CollectProposals(observation, proposals, _rng);
				} catch (Exception ex) {
					throw new BotFeatureException(OrgId, feature.FeatureId, ex);
				}
			}

			BotPlayProposal? best = BotPlayArbitration.SelectBest(proposals);
			if (best != null) {
				CurrentFeatureId = best.FeatureId;
				try {
					EmitPlay(best);
				} finally {
					CurrentFeatureId = "";
				}
				CompleteCycle(currentDate);
				return;
			}

			// Discard only when at least one feature is registered — otherwise there is no
			// consumer for a refreshed hand, and empty-feature bots must stay passive.
			if (_features.Count > 0
				&& BotDiscardHelper.TryDiscardForBetterHand(
					observation, _sink, discardGoldCost: _discardGoldCost)) {
				// Unbounded within the open cycle across ticks until a suitable proposal appears
				// or discard is blocked.
				return;
			}

			CompleteCycle(currentDate);
		}

		void CompleteCycle(DateTime currentDate) {
			_cycleOpen = false;
			_lastCycleCompletedAt = currentDate;
		}

		void EmitPlay(BotPlayProposal proposal) {
			if (proposal.CountryId == "") {
				_sink.PlayOrgCard(proposal.ActionId, proposal.SlotIndex);
			} else {
				_sink.PlayCountryCard(
					proposal.ActionId, proposal.CountryId, proposal.SlotIndex, proposal.TargetCountryId);
			}
		}

		bool TryAcquireCountryCard(IBotObservation observation) {
			if (observation.CountryCardDrawChoices.Count > 0) {
				BotCardDrawChoiceView selected = observation.CountryCardDrawChoices[0];
				double selectedScore = BotDrawIntentScorer.ScoreChoice(selected, observation);
				for (int i = 1; i < observation.CountryCardDrawChoices.Count; i++) {
					BotCardDrawChoiceView candidate = observation.CountryCardDrawChoices[i];
					double candidateScore = BotDrawIntentScorer.ScoreChoice(candidate, observation);
					if (candidateScore > selectedScore
						|| candidateScore == selectedScore && candidate.ChoiceIndex < selected.ChoiceIndex) {
						selected = candidate;
						selectedScore = candidateScore;
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
