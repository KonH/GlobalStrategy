using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	/// <summary>
	/// Dual-side natural-peace Δorg_score EV for declare / prosecute features.
	/// Order per outcome: control shifts on pre-transfer CountryScore, then province-transfer
	/// score mass, then destroy side effect. Mid-band transfer fraction is 0.65 (50–80%).
	/// Peace fractions default to shipped 0.5 winner / 1.0 loser.
	/// </summary>
	public static class WarPeaceOrgScoreEstimator {
		public const double DefaultWinnerControlIncreaseFraction = 0.5;
		public const double DefaultLoserControlDecreaseFraction = 1.0;
		public const double DefaultOccupationTransferFraction = 0.65;
		public const double DefaultAssumedOccupiedShare = 0.35;
		public const double DefaultGoldToOrgScore = 0.001;
		public const double DefaultPeaceGoldPerMonth = 1000.0;
		public const double DefaultAssumedWarMonths = 6.0;
		public const int DefaultMaxControlPool = 100;

		public sealed class Parameters {
			public double WinnerControlIncreaseFraction = DefaultWinnerControlIncreaseFraction;
			public double LoserControlDecreaseFraction = DefaultLoserControlDecreaseFraction;
			public double OccupationTransferFraction = DefaultOccupationTransferFraction;
			public double AssumedOccupiedShare = DefaultAssumedOccupiedShare;
			public double GoldToOrgScore = DefaultGoldToOrgScore;
			public double PeaceGoldPerMonth = DefaultPeaceGoldPerMonth;
			public double AssumedWarMonths = DefaultAssumedWarMonths;
			public int MaxControlPool = DefaultMaxControlPool;
		}

		public static double Estimate(
			string botOrgId,
			BotCountryView attacker,
			BotCountryView defender,
			IReadOnlyList<BotOrgScoreView> orgScores,
			double attackerWinProbability,
			double goldCost,
			Parameters? parameters = null) {
			Parameters p = parameters ?? new Parameters();
			double winP = Clamp01(attackerWinProbability);
			double loseP = 1.0 - winP;

			double whenAttackerWins = ScoreDeltaForOutcome(
				botOrgId, attacker, defender, orgScores, attackerIsWinner: true, p);
			double whenAttackerLoses = ScoreDeltaForOutcome(
				botOrgId, attacker, defender, orgScores, attackerIsWinner: false, p);

			double goldCostPenalty = goldCost * p.GoldToOrgScore;
			double botShareOnWinnerAfterBoost = BotControlShareAfterWinnerBoost(botOrgId, attacker, p);
			double goldSpoils = winP
				* botShareOnWinnerAfterBoost
				* p.PeaceGoldPerMonth
				* p.AssumedWarMonths
				* p.GoldToOrgScore;

			return winP * whenAttackerWins + loseP * whenAttackerLoses - goldCostPenalty + goldSpoils;
		}

		static double ScoreDeltaForOutcome(
			string botOrgId,
			BotCountryView attacker,
			BotCountryView defender,
			IReadOnlyList<BotOrgScoreView> orgScores,
			bool attackerIsWinner,
			Parameters p) {
			BotCountryView winner = attackerIsWinner ? attacker : defender;
			BotCountryView loser = attackerIsWinner ? defender : attacker;

			int botCtrlWinnerBefore = ControlOf(winner, botOrgId);
			int botCtrlLoserBefore = ControlOf(loser, botOrgId);
			int totalWinnerBefore = Math.Max(winner.TotalControl, SumControl(winner));
			int totalLoserBefore = Math.Max(loser.TotalControl, SumControl(loser));

			int botBoost = WinnerBoostDelta(botCtrlWinnerBefore, totalWinnerBefore, p);
			int botCut = LoserCutDelta(botCtrlLoserBefore, p);
			int botCtrlWinnerAfter = botCtrlWinnerBefore + botBoost;
			int botCtrlLoserAfter = botCtrlLoserBefore - botCut;

			// 1) Control-shift term on pre-transfer CountryScore.
			double delta = 0.0;
			delta += (botBoost / 100.0) * winner.CountryScore;
			delta += (-botCut / 100.0) * loser.CountryScore;

			string? leadRivalId = FindLeadRivalOrgId(botOrgId, orgScores);
			double rivalBefore = 0.0;
			double rivalAfter = 0.0;
			if (leadRivalId != null) {
				int rivalCtrlW = ControlOf(winner, leadRivalId);
				int rivalCtrlL = ControlOf(loser, leadRivalId);
				rivalBefore = (rivalCtrlW / 100.0) * winner.CountryScore
					+ (rivalCtrlL / 100.0) * loser.CountryScore;

				int rivalBoost = WinnerBoostDelta(rivalCtrlW, totalWinnerBefore, p);
				int rivalCut = LoserCutDelta(rivalCtrlL, p);
				int rivalCtrlWAfter = rivalCtrlW + rivalBoost;
				int rivalCtrlLAfter = rivalCtrlL - rivalCut;
				rivalAfter = (rivalCtrlWAfter / 100.0) * winner.CountryScore
					+ (rivalCtrlLAfter / 100.0) * loser.CountryScore;
			}

			// 2) Occupation / province-transfer EV (mid-band 0.65).
			double eligible = loser.OccupiedOwnedProvinceCount > 0
				? loser.OccupiedOwnedProvinceCount
				: p.AssumedOccupiedShare * Math.Max(loser.OwnedProvinceCount, 0);
			double transferred = p.OccupationTransferFraction * eligible;
			double scorePerProvince = loser.CountryScore / Math.Max(loser.OwnedProvinceCount, 1);
			double transferMass = scorePerProvince * transferred;
			delta += (botCtrlWinnerAfter / 100.0) * transferMass;
			delta += (botCtrlLoserAfter / 100.0) * (-transferMass);

			if (leadRivalId != null) {
				int rivalCtrlW = ControlOf(winner, leadRivalId);
				int rivalCtrlL = ControlOf(loser, leadRivalId);
				int rivalBoost = WinnerBoostDelta(rivalCtrlW, totalWinnerBefore, p);
				int rivalCut = LoserCutDelta(rivalCtrlL, p);
				int rivalCtrlWAfter = rivalCtrlW + rivalBoost;
				int rivalCtrlLAfter = rivalCtrlL - rivalCut;
				rivalAfter = (rivalCtrlWAfter / 100.0) * (winner.CountryScore + transferMass)
					+ (rivalCtrlLAfter / 100.0) * (loser.CountryScore - transferMass);
			}

			// 3) Destroy side effect: remaining loser owned provinces ≤ 0.
			double remainingProvinces = Math.Max(loser.OwnedProvinceCount, 0) - transferred;
			if (remainingProvinces <= 0) {
				delta += (botCtrlLoserAfter / 100.0) * (-(loser.CountryScore - transferMass));
				if (leadRivalId != null) {
					int rivalCtrlL = ControlOf(loser, leadRivalId);
					int rivalCut = LoserCutDelta(rivalCtrlL, p);
					int rivalCtrlLAfter = rivalCtrlL - rivalCut;
					// Wipe remaining loser contribution for rival after transfer.
					double loserRemain = Math.Max(0.0, loser.CountryScore - transferMass);
					rivalAfter -= (rivalCtrlLAfter / 100.0) * loserRemain;
				}
			}

			// 4) Rival-hurt: bot gains when lead rival's score contribution falls.
			if (leadRivalId != null) {
				delta += rivalBefore - rivalAfter;
			}

			return delta;
		}

		static double BotControlShareAfterWinnerBoost(string botOrgId, BotCountryView winnerCountry, Parameters p) {
			int botCtrl = ControlOf(winnerCountry, botOrgId);
			int total = Math.Max(winnerCountry.TotalControl, SumControl(winnerCountry));
			int boost = WinnerBoostDelta(botCtrl, total, p);
			return (botCtrl + boost) / 100.0;
		}

		static int WinnerBoostDelta(int orgControl, int totalControl, Parameters p) {
			if (orgControl <= 0) {
				return 0;
			}
			int desired = (int)Math.Round(orgControl * p.WinnerControlIncreaseFraction, MidpointRounding.AwayFromZero);
			if (desired <= 0) {
				return 0;
			}
			int room = p.MaxControlPool - totalControl;
			int orgRoom = 100 - orgControl;
			return Math.Max(0, Math.Min(desired, Math.Min(room, orgRoom)));
		}

		static int LoserCutDelta(int orgControl, Parameters p) {
			if (orgControl <= 0) {
				return 0;
			}
			int desired = (int)Math.Round(orgControl * p.LoserControlDecreaseFraction, MidpointRounding.AwayFromZero);
			return Math.Max(0, Math.Min(desired, orgControl));
		}

		static int ControlOf(BotCountryView country, string orgId) {
			if (country.ControlByOrg != null) {
				for (int i = 0; i < country.ControlByOrg.Count; i++) {
					if (country.ControlByOrg[i].OrgId == orgId) {
						return country.ControlByOrg[i].Control;
					}
				}
			}
			return 0;
		}

		static int SumControl(BotCountryView country) {
			int sum = 0;
			if (country.ControlByOrg == null) {
				return sum;
			}
			for (int i = 0; i < country.ControlByOrg.Count; i++) {
				sum += country.ControlByOrg[i].Control;
			}
			return sum;
		}

		static string? FindLeadRivalOrgId(string botOrgId, IReadOnlyList<BotOrgScoreView> orgScores) {
			string? bestId = null;
			double bestScore = double.NegativeInfinity;
			for (int i = 0; i < orgScores.Count; i++) {
				BotOrgScoreView row = orgScores[i];
				if (row.OrgId == botOrgId) {
					continue;
				}
				if (bestId == null
					|| row.OrgScore > bestScore
					|| row.OrgScore == bestScore && string.CompareOrdinal(row.OrgId, bestId) < 0) {
					bestId = row.OrgId;
					bestScore = row.OrgScore;
				}
			}
			return bestId;
		}

		static double Clamp01(double v) {
			if (v < 0) {
				return 0;
			}
			if (v > 1) {
				return 1;
			}
			return v;
		}
	}
}
