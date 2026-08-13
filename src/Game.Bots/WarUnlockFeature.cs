using System;
using System.Collections.Generic;
using GS.Game.Systems;

namespace GS.Game.Bots {
	/// <summary>
	/// Proposes discounted unlock plays (rivalry / opinion) toward a profitable declare or
	/// sell_arms path. Consumes Child D's WarPeaceOrgScoreEstimator only — no private fork.
	/// Emits at most one best proposal per CollectProposals cycle.
	/// </summary>
	public sealed class WarUnlockFeature : IBotFeature {
		public const string Id = "warUnlock";

		public const string MakeRivalActionId = "make_rival";
		public const string ImproveDiplomacyActionId = "improve_diplomacy_advisor_opinion";
		public const string ImproveMilitaryActionId = "improve_military_advisor_opinion";
		public const string ImproveRulerActionId = "improve_ruler_opinion";

		public const string RoleDiplomacy = "diplomacy_advisor";
		public const string RoleMilitary = "military_advisor";
		public const string RoleRuler = "ruler";

		public const double DiplomacyGateForRival = 30.0;
		public const double DeclareOpinionGate = 50.0;
		public const double SellArmsMilGate = 80.0;
		public const double DiplomacyOpinionBoost = 50.0;
		public const double MilitaryOpinionBoost = 50.0;
		public const double RulerOpinionBoost = 25.0;
		public const double DeclareWarGoldCost = 150.0;
		public const double SellArmsGoldCost = 175.0;

		readonly double _minGoldReserve;
		readonly double _unlockPathDiscount;
		readonly double _unlockStepDecay;
		readonly double _unlockGoldPenaltyWeight;
		readonly double _goldToOrgScore;
		readonly double _assumedOccupiedShare;
		readonly double _peaceGoldPerMonth;
		readonly double _assumedWarMonths;
		readonly double _sellArmsDamageBonusPercent;
		readonly int _maxControlPool;
		readonly WarPeaceOrgScoreEstimator.Parameters _peaceParams;

		public WarUnlockFeature(IReadOnlyDictionary<string, double> parameters, int maxControlPool) {
			_minGoldReserve = Param(parameters, "minGoldReserve", 0.0);
			_unlockPathDiscount = Param(parameters, "unlockPathDiscount", 0.4);
			_unlockStepDecay = Param(parameters, "unlockStepDecay", 0.7);
			_unlockGoldPenaltyWeight = Param(parameters, "unlockGoldPenaltyWeight", 0.0);
			_goldToOrgScore = Param(parameters, "goldToOrgScore", WarPeaceOrgScoreEstimator.DefaultGoldToOrgScore);
			_assumedOccupiedShare = Param(parameters, "assumedOccupiedShare", WarPeaceOrgScoreEstimator.DefaultAssumedOccupiedShare);
			_peaceGoldPerMonth = Param(parameters, "peaceGoldPerMonth", WarPeaceOrgScoreEstimator.DefaultPeaceGoldPerMonth);
			_assumedWarMonths = Param(parameters, "assumedWarMonths", WarPeaceOrgScoreEstimator.DefaultAssumedWarMonths);
			_sellArmsDamageBonusPercent = Param(parameters, "sellArmsDamageBonusPercent", 30.0);
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

		public void CollectProposals(IBotObservation obs, IList<BotPlayProposal> proposals, Random rng) {
			_ = rng;
			BotPlayProposal? best = null;
			double bestScore = double.NegativeInfinity;

			for (int ci = 0; ci < obs.Countries.Count; ci++) {
				BotCountryView country = obs.Countries[ci];
				if (country.IsDestroyed) {
					continue;
				}
				for (int hi = 0; hi < country.Hand.Count; hi++) {
					BotCardView card = country.Hand[hi];
					if (!TryScoreUnlock(obs, country, card, out BotPlayProposal candidate, out double score)) {
						continue;
					}
					if (IsBetterCandidate(candidate, score, best, bestScore)) {
						best = candidate;
						bestScore = score;
					}
				}
			}

			if (best != null) {
				proposals.Add(best);
			}
		}

		bool TryScoreUnlock(
			IBotObservation obs,
			BotCountryView country,
			BotCardView card,
			out BotPlayProposal proposal,
			out double score) {
			proposal = null!;
			score = 0;
			if (!card.IsPlayable || !IsOwnedUnlockAction(card.ActionId)) {
				return false;
			}
			if (obs.Gold - card.GoldCost < _minGoldReserve) {
				return false;
			}

			int k;
			double warDelta;
			string targetCountryId = card.TargetCountryId ?? "";

			if (card.ActionId == MakeRivalActionId) {
				if (!TryScoreMakeRival(obs, country, card, out warDelta, out k)) {
					return false;
				}
			} else if (card.ActionId == ImproveDiplomacyActionId) {
				if (!TryScoreDiplomacyOpinion(obs, country, out warDelta, out k, out targetCountryId)) {
					return false;
				}
			} else if (card.ActionId == ImproveMilitaryActionId || card.ActionId == ImproveRulerActionId) {
				if (!TryScoreOpinionImprover(obs, country, card, out warDelta, out k, out targetCountryId)) {
					return false;
				}
			} else {
				return false;
			}

			if (warDelta <= 0) {
				return false;
			}

			score = PathScore(warDelta, k, card.GoldCost);
			proposal = new BotPlayProposal {
				FeatureId = FeatureId,
				ActionId = card.ActionId,
				CountryId = card.CountryId,
				TargetCountryId = targetCountryId,
				SlotIndex = card.SlotIndex,
				EstimatedDeltaOrgScore = score
			};
			return true;
		}

		bool TryScoreMakeRival(
			IBotObservation obs,
			BotCountryView country,
			BotCardView card,
			out double warDelta,
			out int k) {
			warDelta = 0;
			k = 1;
			if (string.IsNullOrEmpty(card.TargetCountryId)) {
				return false;
			}
			if (IsRival(country, card.TargetCountryId)) {
				return false;
			}
			BotCountryView? target = ResolveCountry(obs, card.TargetCountryId);
			if (target == null || target.IsDestroyed) {
				return false;
			}
			if (country.IsAtWar || target.IsAtWar) {
				return false;
			}

			warDelta = EstimateDeclarePathDelta(obs, country, target);
			if (warDelta <= 0) {
				return false;
			}

			k = 1 + (NeedsDeclareOpinionStep(country) ? 1 : 0);
			return true;
		}

		bool TryScoreDiplomacyOpinion(
			IBotObservation obs,
			BotCountryView country,
			out double warDelta,
			out int k,
			out string pathTargetId) {
			warDelta = 0;
			k = 1;
			pathTargetId = "";
			if (OpinionOf(country, RoleDiplomacy) >= DiplomacyGateForRival) {
				return false;
			}

			if (!TryBestProfitableMakeRivalTarget(obs, country, out BotCountryView bestTarget, out warDelta)) {
				return false;
			}

			pathTargetId = bestTarget.CountryId;
			// diplo → make_rival → (optional declare opinion)
			k = 2 + (NeedsDeclareOpinionStep(country) ? 1 : 0);
			return true;
		}

		bool TryScoreOpinionImprover(
			IBotObservation obs,
			BotCountryView country,
			BotCardView card,
			out double warDelta,
			out int k,
			out string pathTargetId) {
			warDelta = 0;
			k = 1;
			pathTargetId = "";

			double ruler = OpinionOf(country, RoleRuler);
			double mil = OpinionOf(country, RoleMilitary);
			double maxRm = Math.Max(ruler, mil);

			// Wartime sell_arms path (military improver only).
			if (card.ActionId == ImproveMilitaryActionId
				&& country.IsAtWar
				&& !string.IsNullOrEmpty(country.WarOpponentCountryId)
				&& mil < SellArmsMilGate) {
				BotCountryView? opponent = ResolveCountry(obs, country.WarOpponentCountryId);
				if (opponent != null
					&& !opponent.IsDestroyed
					&& TryEstimateProsecutePathDelta(obs, country, opponent, out double prosecuteDelta)
					&& prosecuteDelta > 0) {
					warDelta = prosecuteDelta;
					pathTargetId = opponent.CountryId;
					k = 1;
					return true;
				}
			}

			// Peacetime declare opinion gate.
			if (country.IsAtWar || maxRm >= DeclareOpinionGate) {
				return false;
			}
			if (!ImproverRaisesDeclareMax(card.ActionId, ruler, mil)) {
				return false;
			}

			if (!TryBestProfitableDeclareTarget(obs, country, out BotCountryView bestTarget, out warDelta)) {
				return false;
			}

			pathTargetId = bestTarget.CountryId;
			bool needRival = !IsRival(country, bestTarget.CountryId);
			bool needDiplo = needRival && OpinionOf(country, RoleDiplomacy) < DiplomacyGateForRival;
			k = 1 + (needRival ? 1 : 0) + (needDiplo ? 1 : 0);
			return true;
		}

		bool TryBestProfitableMakeRivalTarget(
			IBotObservation obs,
			BotCountryView country,
			out BotCountryView bestTarget,
			out double bestDelta) {
			bestTarget = null!;
			bestDelta = 0;
			bool found = false;
			for (int i = 0; i < obs.Countries.Count; i++) {
				BotCountryView other = obs.Countries[i];
				if (other.CountryId == country.CountryId || other.IsDestroyed) {
					continue;
				}
				if (IsRival(country, other.CountryId) || country.IsAtWar || other.IsAtWar) {
					continue;
				}
				double delta = EstimateDeclarePathDelta(obs, country, other);
				if (delta <= 0) {
					continue;
				}
				if (!found
					|| delta > bestDelta
					|| delta == bestDelta && string.CompareOrdinal(other.CountryId, bestTarget.CountryId) < 0) {
					found = true;
					bestTarget = other;
					bestDelta = delta;
				}
			}
			return found;
		}

		bool TryBestProfitableDeclareTarget(
			IBotObservation obs,
			BotCountryView country,
			out BotCountryView bestTarget,
			out double bestDelta) {
			bestTarget = null!;
			bestDelta = 0;
			bool found = false;
			for (int i = 0; i < obs.Countries.Count; i++) {
				BotCountryView other = obs.Countries[i];
				if (other.CountryId == country.CountryId || other.IsDestroyed) {
					continue;
				}
				if (country.IsAtWar || other.IsAtWar) {
					continue;
				}
				double delta = EstimateDeclarePathDelta(obs, country, other);
				if (delta <= 0) {
					continue;
				}
				if (!found
					|| delta > bestDelta
					|| delta == bestDelta && string.CompareOrdinal(other.CountryId, bestTarget.CountryId) < 0) {
					found = true;
					bestTarget = other;
					bestDelta = delta;
				}
			}
			return found;
		}

		double EstimateDeclarePathDelta(IBotObservation obs, BotCountryView attacker, BotCountryView defender) {
			int winPercent = WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits,
				attacker.Damage,
				attacker.Durability,
				defender.Recruits,
				defender.Damage,
				defender.Durability);
			return WarPeaceOrgScoreEstimator.Estimate(
				obs.OrgId,
				attacker,
				defender,
				obs.OrgScores,
				attackerWinProbability: winPercent / 100.0,
				goldCost: DeclareWarGoldCost,
				parameters: _peaceParams);
		}

		bool TryEstimateProsecutePathDelta(
			IBotObservation obs,
			BotCountryView preferred,
			BotCountryView opponent,
			out double prosecuteDelta) {
			// Prefer treating the unlock country as attacker for EV; if live progress suggests
			// the opponent is the war attacker, swap for win-% then map preferred win p.
			bool preferredIsAttacker = preferred.OwnWarProgress >= 0;
			BotCountryView attacker = preferredIsAttacker ? preferred : opponent;
			BotCountryView defender = preferredIsAttacker ? opponent : preferred;

			int pBefore = WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits,
				attacker.Damage,
				attacker.Durability,
				defender.Recruits,
				defender.Damage,
				defender.Durability);

			double bonus = preferred.TroopsDamageBonusPercent;
			double factor = (100.0 + bonus + _sellArmsDamageBonusPercent) / Math.Max(100.0 + bonus, 1.0);
			double preferredDamageAfter = preferred.Damage * factor;

			double attDamage = preferredIsAttacker ? preferredDamageAfter : attacker.Damage;
			double defDamage = preferredIsAttacker ? defender.Damage : preferredDamageAfter;
			int pAfter = WarWinChanceEstimator.EstimateAttackerWinPercent(
				attacker.Recruits,
				attDamage,
				attacker.Durability,
				defender.Recruits,
				defDamage,
				defender.Durability);

			double prefWinBefore = preferredIsAttacker ? pBefore / 100.0 : 1.0 - pBefore / 100.0;
			double prefWinAfter = preferredIsAttacker ? pAfter / 100.0 : 1.0 - pAfter / 100.0;

			// Terminal path value after sell_arms: EV at post-arms win%, costing sell_arms gold.
			// For unlock scoring we use the post-arms peace EV (path terminal), not the lift alone.
			double afterEv = WarPeaceOrgScoreEstimator.Estimate(
				obs.OrgId,
				preferred,
				opponent,
				obs.OrgScores,
				attackerWinProbability: prefWinAfter,
				goldCost: SellArmsGoldCost,
				parameters: _peaceParams);
			_ = prefWinBefore;
			prosecuteDelta = afterEv;
			return true;
		}

		double PathScore(double warDelta, int k, double goldCost) {
			int steps = Math.Max(k, 1);
			double decay = Math.Pow(_unlockStepDecay, steps - 1);
			double goldSpentFraction = _unlockGoldPenaltyWeight * goldCost * _goldToOrgScore;
			return _unlockPathDiscount * decay * warDelta - goldSpentFraction;
		}

		static bool NeedsDeclareOpinionStep(BotCountryView country) {
			double maxRm = Math.Max(OpinionOf(country, RoleRuler), OpinionOf(country, RoleMilitary));
			return maxRm < DeclareOpinionGate;
		}

		static bool ImproverRaisesDeclareMax(string actionId, double ruler, double mil) {
			double before = Math.Max(ruler, mil);
			if (before >= DeclareOpinionGate) {
				return false;
			}
			if (actionId == ImproveMilitaryActionId) {
				return Math.Max(ruler, mil + MilitaryOpinionBoost) > before;
			}
			if (actionId == ImproveRulerActionId) {
				return Math.Max(ruler + RulerOpinionBoost, mil) > before;
			}
			return false;
		}

		static bool IsOwnedUnlockAction(string actionId) {
			return actionId == MakeRivalActionId
				|| actionId == ImproveDiplomacyActionId
				|| actionId == ImproveMilitaryActionId
				|| actionId == ImproveRulerActionId;
		}

		static bool IsRival(BotCountryView country, string otherCountryId) {
			IReadOnlyList<string> rivals = country.RivalCountryIds;
			for (int i = 0; i < rivals.Count; i++) {
				if (rivals[i] == otherCountryId) {
					return true;
				}
			}
			return false;
		}

		static double OpinionOf(BotCountryView country, string roleId) {
			IReadOnlyList<BotCountryCharacterView> chars = country.Characters;
			for (int i = 0; i < chars.Count; i++) {
				if (chars[i].RoleId == roleId) {
					return chars[i].OpinionOfMyOrg;
				}
			}
			return 0;
		}

		static BotCountryView? ResolveCountry(IBotObservation obs, string countryId) {
			BotCountryView? direct = obs.GetCountry(countryId);
			if (direct != null) {
				return direct;
			}
			for (int i = 0; i < obs.Countries.Count; i++) {
				if (obs.Countries[i].CountryId == countryId) {
					return obs.Countries[i];
				}
			}
			return null;
		}

		static bool IsBetterCandidate(
			BotPlayProposal candidate,
			double candidateScore,
			BotPlayProposal? best,
			double bestScore) {
			if (best == null) {
				return true;
			}
			if (candidateScore > bestScore) {
				return true;
			}
			if (candidateScore < bestScore) {
				return false;
			}
			// Tie-break: lower SlotIndex, then ordinal ActionId, then ordinal TargetCountryId.
			if (candidate.SlotIndex != best.SlotIndex) {
				return candidate.SlotIndex < best.SlotIndex;
			}
			int actionCmp = string.CompareOrdinal(candidate.ActionId, best.ActionId);
			if (actionCmp != 0) {
				return actionCmp < 0;
			}
			return string.CompareOrdinal(candidate.TargetCountryId, best.TargetCountryId) < 0;
		}

		static double Param(IReadOnlyDictionary<string, double> parameters, string key, double fallback) {
			return parameters.TryGetValue(key, out var v) ? v : fallback;
		}
	}
}
