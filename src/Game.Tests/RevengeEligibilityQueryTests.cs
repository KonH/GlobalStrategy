using ECS;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class RevengeEligibilityQueryTests {
		[Fact]
		void not_eligible_by_default() {
			var world = new World();
			Assert.False(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
		}

		[Fact]
		void set_eligible_makes_the_pair_eligible() {
			var world = new World();
			RevengeEligibilityQuery.SetEligible(world, "Prussia", "France");
			Assert.True(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
		}

		[Fact]
		void set_eligible_is_idempotent() {
			var world = new World();
			RevengeEligibilityQuery.SetEligible(world, "Prussia", "France");
			RevengeEligibilityQuery.SetEligible(world, "Prussia", "France");
			Assert.True(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
		}

		[Fact]
		void clear_eligible_removes_only_the_matching_pair() {
			var world = new World();
			RevengeEligibilityQuery.SetEligible(world, "Prussia", "France");
			RevengeEligibilityQuery.SetEligible(world, "France", "Prussia");

			RevengeEligibilityQuery.ClearEligible(world, "Prussia", "France");

			Assert.False(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
			Assert.True(RevengeEligibilityQuery.IsEligible(world, "France", "Prussia"));
		}

		[Fact]
		void on_war_resolved_grants_eligibility_to_the_loser_only() {
			var world = new World();
			RevengeEligibilityQuery.OnWarResolved(world, winnerCountryId: "France", loserCountryId: "Prussia");

			Assert.True(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
			Assert.False(RevengeEligibilityQuery.IsEligible(world, "France", "Prussia"));
		}

		[Fact]
		void on_war_resolved_keeps_eligibility_across_repeat_losses() {
			var world = new World();
			RevengeEligibilityQuery.OnWarResolved(world, winnerCountryId: "France", loserCountryId: "Prussia");
			RevengeEligibilityQuery.OnWarResolved(world, winnerCountryId: "France", loserCountryId: "Prussia");

			Assert.True(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
		}

		[Fact]
		void on_war_resolved_clears_the_winners_own_eligibility_against_the_loser() {
			var world = new World();
			// Prussia previously lost to France, then avenges it by winning the rematch.
			RevengeEligibilityQuery.SetEligible(world, "Prussia", "France");

			RevengeEligibilityQuery.OnWarResolved(world, winnerCountryId: "Prussia", loserCountryId: "France");

			Assert.False(RevengeEligibilityQuery.IsEligible(world, "Prussia", "France"));
			Assert.True(RevengeEligibilityQuery.IsEligible(world, "France", "Prussia"));
		}
	}
}
