using System;
using System.Collections.Generic;
using GS.Game.Systems;

namespace GS.Game.Bots {
	public sealed class WarDeclareFeature : IBotFeature {
		public const string Id = "warDeclare";
		public const string DeclareWarActionId = "declare_war";
		public const string DeclareRevengeWarActionId = "declare_revenge_war";

		readonly double _minGoldReserve;
		readonly double _highEvDipMinDelta;
		readonly double _minWinPercent;
		readonly double _targetBiasWeight;
		readonly double _goldToOrgScore;
		readonly double _assumedOccupiedShare;
		readonly double _revengePendingDamageBonusPercent;
		readonly double _revengePendingDurabilityBonusPercent;
		readonly double _peaceGoldPerMonth;
		readonly double _assumedWarMonths;
		readonly int _maxControlPool;
		readonly WarPeaceOrgScoreEstimator.Parameters _peaceParams;

		public WarDeclareFeature(IReadOnlyDictionary<string, double> parameters, int maxControlPool) {
			_minGoldReserve = Param(parameters, "minGoldReserve", 0.0);
			_highEvDipMinDelta = Param(parameters, "highEvDipMinDelta", 0.0);
			_minWinPercent = Param(parameters, "minWinPercent", 40.0);
			_targetBiasWeight = Param(parameters, "targetBiasWeight", 0.10);
			_goldToOrgScore = Param(parameters, "goldToOrgScore", WarPeaceOrgScoreEstimator.DefaultGoldToOrgScore);
			_assumedOccupiedShare = Param(parameters, "assumedOccupiedShare", WarPeaceOrgScoreEstimator.DefaultAssumedOccupiedShare);
			_revengePendingDamageBonusPercent = Param(parameters, "revengePendingDamageBonusPercent", 10.0);
			_revengePendingDurabilityBonusPercent = Param(parameters, "revengePendingDurabilityBonusPercent", 5.0);
			_peaceGoldPerMonth = Param(parameters, "peaceGoldPerMonth", WarPeaceOrgScoreEstimator.DefaultPeaceGoldPerMonth);
			_assumedWarMonths = Param(parameters, "assumedWarMonths", WarPeaceOrgScoreEstimator.DefaultAssumedWarMonths);
			_maxControlPool = maxControlPool > 0 ? maxControlPool : WarPeaceOrgScoreEstimator.DefaultMaxControlPool;
			// revengeArbitrationMultiplier (default 1.20) is owned by BotPlayArbitration — do not apply here.
			_ = Param(parameters, "revengeArbitrationMultiplier", BotPlayArbitration.RevengeSoftWeight);
			_peaceParams = new WarPeaceOrgScoreEstimator.Parameters {
				AssumedOccupiedShare = _assumedOccupiedShare,
				GoldToOrgScore = _goldToOrgScore,
				PeaceGoldPerMonth = _peaceGoldPerMonth,
				AssumedWarMonths = _assumedWarMonths,
				MaxControlPool = _maxControlPool
			};
		}

		public string FeatureId => Id;

		public void CollectProposals(IBotObservation obs, IList<BotPlayProposal> proposals, Random rng) {
			_ = rng;
			foreach (var country in obs.Countries) {
				foreach (var card in country.Hand) {
					if (TryBuildProposal(obs, card, out BotPlayProposal proposal)) {
						proposals.Add(proposal);
					}
				}
			}
		}

		bool TryBuildProposal(IBotObservation obs, BotCardView card, out BotPlayProposal proposal) {
			proposal = null!;
			bool isRevenge = card.ActionId == DeclareRevengeWarActionId;
			if (!card.IsPlayable || (card.ActionId != DeclareWarActionId && !isRevenge)) {
				return false;
			}
			if (string.IsNullOrEmpty(card.TargetCountryId)) {
				return false;
			}

			BotCountryView? attacker = obs.GetCountry(card.CountryId) ?? FindCountry(obs, card.CountryId);
			BotCountryView? defender = obs.GetCountry(card.TargetCountryId) ?? FindCountry(obs, card.TargetCountryId);
			if (attacker == null || defender == null) {
				return false;
			}
			if (attacker.IsDestroyed || defender.IsDestroyed || attacker.IsAtWar || defender.IsAtWar) {
				return false;
			}

			double pendingDamage = isRevenge ? _revengePendingDamageBonusPercent : 0.0;
			double pendingDurability = isRevenge ? _revengePendingDurabilityBonusPercent : 0.0;
			int winPercent = WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits,
				attacker.Damage,
				attacker.Durability,
				defender.Recruits,
				defender.Damage,
				defender.Durability,
				pendingDamage,
				pendingDurability);
			if (winPercent < _minWinPercent) {
				return false;
			}

			double rawDelta = WarPeaceOrgScoreEstimator.Estimate(
				obs.OrgId,
				attacker,
				defender,
				obs.OrgScores,
				attackerWinProbability: winPercent / 100.0,
				goldCost: card.GoldCost,
				parameters: _peaceParams);
			if (rawDelta <= 0) {
				return false;
			}

			if (obs.Gold - card.GoldCost < _minGoldReserve
				&& !(rawDelta >= _highEvDipMinDelta && rawDelta > 0)) {
				return false;
			}

			proposal = new BotPlayProposal {
				FeatureId = FeatureId,
				ActionId = card.ActionId,
				CountryId = card.CountryId,
				TargetCountryId = card.TargetCountryId,
				SlotIndex = card.SlotIndex,
				EstimatedDeltaOrgScore = rawDelta,
				ApplyRevengeSoftWeight = isRevenge,
				TargetBiasMultiplier = ComputeTargetBiasMultiplier(defender, obs.OrgId)
			};
			return true;
		}

		double ComputeTargetBiasMultiplier(BotCountryView target, string botOrgId) {
			int botControl = 0;
			int maxOther = 0;
			if (target.ControlByOrg != null && target.ControlByOrg.Count > 0) {
				for (int i = 0; i < target.ControlByOrg.Count; i++) {
					BotControlShare share = target.ControlByOrg[i];
					if (share.OrgId == botOrgId) {
						botControl = share.Control;
					} else if (share.Control > maxOther) {
						maxOther = share.Control;
					}
				}
			} else {
				botControl = target.MyControl;
				maxOther = Math.Max(0, target.TotalControl - botControl);
			}
			if (botControl == 0 || maxOther > botControl) {
				return 1.0 + _targetBiasWeight;
			}
			return 1.0;
		}

		static BotCountryView? FindCountry(IBotObservation obs, string countryId) {
			for (int i = 0; i < obs.Countries.Count; i++) {
				if (obs.Countries[i].CountryId == countryId) {
					return obs.Countries[i];
				}
			}
			return null;
		}

		static double Param(IReadOnlyDictionary<string, double> parameters, string key, double fallback) {
			return parameters.TryGetValue(key, out var v) ? v : fallback;
		}
	}
}
