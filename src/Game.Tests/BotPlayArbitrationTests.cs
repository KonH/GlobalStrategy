using System.Collections.Generic;
using GS.Game.Bots;
using Xunit;

namespace GS.Game.Tests {
	public class BotPlayArbitrationTests {
		static BotPlayProposal Proposal(
			string featureId,
			string actionId,
			double delta,
			bool revenge = false,
			double targetBias = 1.0,
			string countryId = "",
			string targetCountryId = "",
			int slotIndex = 0) {
			return new BotPlayProposal {
				FeatureId = featureId,
				ActionId = actionId,
				CountryId = countryId,
				TargetCountryId = targetCountryId,
				SlotIndex = slotIndex,
				EstimatedDeltaOrgScore = delta,
				ApplyRevengeSoftWeight = revenge,
				TargetBiasMultiplier = targetBias
			};
		}

		[Fact]
		void revenge_soft_weight_can_outrank_higher_raw_ordinary_delta() {
			var ordinary = Proposal("warDeclare", "declare_war", delta: 10.0);
			var revenge = Proposal("warDeclare", "declare_revenge_war", delta: 9.0, revenge: true);
			Assert.True(BotPlayArbitration.ArbitrationScore(ordinary) > revenge.EstimatedDeltaOrgScore);
			Assert.True(BotPlayArbitration.ArbitrationScore(revenge) > BotPlayArbitration.ArbitrationScore(ordinary));

			BotPlayProposal? best = BotPlayArbitration.SelectBest(new[] { ordinary, revenge });
			Assert.Same(revenge, best);
		}

		[Fact]
		void non_positive_arbitration_scores_are_excluded() {
			var proposals = new List<BotPlayProposal> {
				Proposal("a", "x", delta: 0),
				Proposal("b", "y", delta: -3),
				Proposal("c", "z", delta: 2)
			};
			BotPlayProposal? best = BotPlayArbitration.SelectBest(proposals);
			Assert.NotNull(best);
			Assert.Equal("c", best!.FeatureId);
		}

		[Fact]
		void equal_scores_break_ties_deterministically_by_ordinal_fields() {
			var later = Proposal("beta", "b", delta: 5, countryId: "B", targetCountryId: "T", slotIndex: 1);
			var earlier = Proposal("alpha", "a", delta: 5, countryId: "A", targetCountryId: "S", slotIndex: 0);
			BotPlayProposal? best = BotPlayArbitration.SelectBest(new[] { later, earlier });
			Assert.Same(earlier, best);
		}

		[Fact]
		void revenge_soft_weight_constant_is_locked() {
			Assert.Equal(1.20, BotPlayArbitration.RevengeSoftWeight);
		}
	}
}
