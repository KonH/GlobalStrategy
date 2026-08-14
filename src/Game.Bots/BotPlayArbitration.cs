using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public static class BotPlayArbitration {
		public const double RevengeSoftWeight = 1.20;

		public static double ArbitrationScore(BotPlayProposal p) {
			double m = p.TargetBiasMultiplier > 0 ? p.TargetBiasMultiplier : 1.0;
			double raw = p.EstimatedDeltaOrgScore * m;
			return p.ApplyRevengeSoftWeight ? raw * RevengeSoftWeight : raw;
		}

		public static BotPlayProposal? SelectBest(IReadOnlyList<BotPlayProposal> proposals) {
			BotPlayProposal? best = null;
			double bestScore = 0;
			for (int i = 0; i < proposals.Count; i++) {
				BotPlayProposal candidate = proposals[i];
				double score = ArbitrationScore(candidate);
				if (score <= 0) {
					continue;
				}
				if (best == null || score > bestScore || score == bestScore && IsPreferableTieBreak(candidate, best)) {
					best = candidate;
					bestScore = score;
				}
			}
			return best;
		}

		static bool IsPreferableTieBreak(BotPlayProposal candidate, BotPlayProposal incumbent) {
			int featureCmp = string.CompareOrdinal(candidate.FeatureId, incumbent.FeatureId);
			if (featureCmp != 0) {
				return featureCmp < 0;
			}
			int actionCmp = string.CompareOrdinal(candidate.ActionId, incumbent.ActionId);
			if (actionCmp != 0) {
				return actionCmp < 0;
			}
			int countryCmp = string.CompareOrdinal(candidate.CountryId, incumbent.CountryId);
			if (countryCmp != 0) {
				return countryCmp < 0;
			}
			int targetCmp = string.CompareOrdinal(candidate.TargetCountryId, incumbent.TargetCountryId);
			if (targetCmp != 0) {
				return targetCmp < 0;
			}
			return candidate.SlotIndex < incumbent.SlotIndex;
		}
	}
}
