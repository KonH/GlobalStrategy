using System;
using System.Collections.Generic;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Bots {
	public sealed class WarProsecuteFeature : IBotFeature {
		public const string Id = "warProsecute";
		public const string SellArmsActionId = "sell_arms";
		public const string SellArmsDamageBonusEffectId = "sell_arms_damage_bonus_effect";
		public const string ForceWarWinActionId = "force_war_win";
		public const string ForceWarLossActionId = "force_war_loss";
		public const double DefaultSellArmsDamageBonusPercent = 30.0;

		readonly double _minGoldReserve;
		readonly double _minProfitDelta;
		readonly double _minWinPercentAfter;
		readonly double _minWinPercentLift;
		readonly bool _allowHighEvGoldDip;
		readonly double _highEvDipMinDelta;
		readonly double _targetBiasWeight;
		readonly double _goldToOrgScore;
		readonly double _assumedOccupiedShare;
		readonly double _peaceGoldPerMonth;
		readonly double _assumedWarMonths;
		readonly double _sellArmsDamageBonusPercent;
		readonly int _maxControlPool;
		readonly WarPeaceOrgScoreEstimator.Parameters _peaceParams;

		public WarProsecuteFeature(
			IReadOnlyDictionary<string, double> parameters,
			int maxControlPool,
			double sellArmsDamageBonusPercent = DefaultSellArmsDamageBonusPercent) {
			_minGoldReserve = Param(parameters, "minGoldReserve", 0.0);
			_minProfitDelta = Param(parameters, "minProfitDelta", 0.0);
			_minWinPercentAfter = Param(parameters, "minWinPercentAfter", 0.0);
			_minWinPercentLift = Param(parameters, "minWinPercentLift", 0.0);
			_allowHighEvGoldDip = Param(parameters, "allowHighEvGoldDip", 1.0) >= 0.5;
			_highEvDipMinDelta = Param(parameters, "highEvDipMinDelta", 0.0);
			_targetBiasWeight = Param(parameters, "targetBiasWeight", 0.10);
			_goldToOrgScore = ResolveGoldToOrgScore(parameters);
			_assumedOccupiedShare = Param(parameters, "assumedOccupiedShare", WarPeaceOrgScoreEstimator.DefaultAssumedOccupiedShare);
			_peaceGoldPerMonth = Param(parameters, "peaceGoldPerMonth", WarPeaceOrgScoreEstimator.DefaultPeaceGoldPerMonth);
			_assumedWarMonths = Param(parameters, "assumedWarMonths", WarPeaceOrgScoreEstimator.DefaultAssumedWarMonths);
			_sellArmsDamageBonusPercent = Param(parameters, "sellArmsDamageBonusPercent", sellArmsDamageBonusPercent);
			_maxControlPool = maxControlPool > 0 ? maxControlPool : WarPeaceOrgScoreEstimator.DefaultMaxControlPool;
			_peaceParams = new WarPeaceOrgScoreEstimator.Parameters {
				AssumedOccupiedShare = _assumedOccupiedShare,
				GoldToOrgScore = _goldToOrgScore,
				PeaceGoldPerMonth = _peaceGoldPerMonth,
				AssumedWarMonths = _assumedWarMonths,
				MaxControlPool = _maxControlPool
			};
		}

		public string FeatureId => Id;

		public static double ResolveSellArmsDamageBonusPercent(EffectConfig? effectConfig) {
			if (effectConfig == null) {
				return DefaultSellArmsDamageBonusPercent;
			}
			ActionEffectDefinition? def = effectConfig.Find(SellArmsDamageBonusEffectId);
			if (def is CountryResourceModifierEffectParams modifier && modifier.InitialValue > 0) {
				return modifier.InitialValue;
			}
			return DefaultSellArmsDamageBonusPercent;
		}

		public static double SellArmsDamageScaleFactor(double troopsDamageBonusPercent, double sellArmsInitial) {
			double bonus = Math.Max(0.0, troopsDamageBonusPercent);
			double add = Math.Max(0.0, sellArmsInitial);
			return (100.0 + bonus + add) / (100.0 + bonus);
		}

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
			if (card.ActionId == ForceWarWinActionId || card.ActionId == ForceWarLossActionId) {
				return false;
			}
			if (!card.IsPlayable || card.ActionId != SellArmsActionId) {
				return false;
			}

			BotCountryView? preferred = obs.GetCountry(card.CountryId) ?? FindCountry(obs, card.CountryId);
			if (preferred == null || preferred.IsDestroyed || !preferred.IsAtWar) {
				return false;
			}
			if (string.IsNullOrEmpty(preferred.WarOpponentCountryId)) {
				return false;
			}

			BotCountryView? opponent = obs.GetCountry(preferred.WarOpponentCountryId)
				?? FindCountry(obs, preferred.WarOpponentCountryId);
			if (opponent == null || opponent.IsDestroyed) {
				return false;
			}

			bool preferredIsAttacker = ResolvePreferredIsAttacker(preferred, opponent);
			BotCountryView attacker = preferredIsAttacker ? preferred : opponent;
			BotCountryView defender = preferredIsAttacker ? opponent : preferred;

			double factor = SellArmsDamageScaleFactor(preferred.TroopsDamageBonusPercent, _sellArmsDamageBonusPercent);
			double preferredDamageAfter = preferred.Damage * factor;

			double attackerDamageBefore = attacker.Damage;
			double defenderDamageBefore = defender.Damage;
			double attackerDamageAfter = preferredIsAttacker ? preferredDamageAfter : attackerDamageBefore;
			double defenderDamageAfter = preferredIsAttacker ? defenderDamageBefore : preferredDamageAfter;

			// Pending must stay 0 — sell_arms stacks additively via external damage scale, not revenge-replace.
			int pBefore = WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits,
				attackerDamageBefore,
				attacker.Durability,
				defender.Recruits,
				defenderDamageBefore,
				defender.Durability);
			int pAfter = WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits,
				attackerDamageAfter,
				attacker.Durability,
				defender.Recruits,
				defenderDamageAfter,
				defender.Durability);

			int prefWinBefore = preferredIsAttacker ? pBefore : 100 - pBefore;
			int prefWinAfter = preferredIsAttacker ? pAfter : 100 - pAfter;
			int lift = prefWinAfter - prefWinBefore;
			if (prefWinAfter < _minWinPercentAfter || lift < _minWinPercentLift) {
				return false;
			}

			double evBefore = WarPeaceOrgScoreEstimator.Estimate(
				obs.OrgId,
				attacker,
				defender,
				obs.OrgScores,
				attackerWinProbability: pBefore / 100.0,
				goldCost: 0.0,
				parameters: _peaceParams);
			double evAfter = WarPeaceOrgScoreEstimator.Estimate(
				obs.OrgId,
				attacker,
				defender,
				obs.OrgScores,
				attackerWinProbability: pAfter / 100.0,
				goldCost: 0.0,
				parameters: _peaceParams);
			double prosecuteDelta = evAfter - evBefore - card.GoldCost * _goldToOrgScore;
			if (prosecuteDelta <= _minProfitDelta) {
				return false;
			}

			if (obs.Gold - card.GoldCost < _minGoldReserve) {
				if (!_allowHighEvGoldDip || prosecuteDelta < _highEvDipMinDelta || prosecuteDelta <= 0) {
					return false;
				}
			}

			proposal = new BotPlayProposal {
				FeatureId = FeatureId,
				ActionId = card.ActionId,
				CountryId = card.CountryId,
				TargetCountryId = card.TargetCountryId,
				SlotIndex = card.SlotIndex,
				EstimatedDeltaOrgScore = prosecuteDelta,
				ApplyRevengeSoftWeight = false,
				TargetBiasMultiplier = ComputeTargetBiasMultiplier(opponent, obs.OrgId)
			};
			return true;
		}

		static bool ResolvePreferredIsAttacker(BotCountryView preferred, BotCountryView opponent) {
			if (preferred.OwnWarProgress > 0) {
				return true;
			}
			if (preferred.OwnWarProgress < 0) {
				return false;
			}
			if (opponent.OwnWarProgress < 0) {
				return true;
			}
			if (opponent.OwnWarProgress > 0) {
				return false;
			}
			return true;
		}

		double ComputeTargetBiasMultiplier(BotCountryView enemy, string botOrgId) {
			int botControl = 0;
			int maxOther = 0;
			if (enemy.ControlByOrg != null && enemy.ControlByOrg.Count > 0) {
				for (int i = 0; i < enemy.ControlByOrg.Count; i++) {
					BotControlShare share = enemy.ControlByOrg[i];
					if (share.OrgId == botOrgId) {
						botControl = share.Control;
					} else if (share.Control > maxOther) {
						maxOther = share.Control;
					}
				}
			} else {
				botControl = enemy.MyControl;
				maxOther = Math.Max(0, enemy.TotalControl - botControl);
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

		static double ResolveGoldToOrgScore(IReadOnlyDictionary<string, double> parameters) {
			if (parameters.TryGetValue("goldToScoreWeight", out double weight)) {
				return weight;
			}
			return Param(parameters, "goldToOrgScore", WarPeaceOrgScoreEstimator.DefaultGoldToOrgScore);
		}

		static double Param(IReadOnlyDictionary<string, double> parameters, string key, double fallback) {
			return parameters.TryGetValue(key, out var v) ? v : fallback;
		}
	}
}
