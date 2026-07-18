using ECS;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	// CountryScoreSystem is trimmed to GetScore only — country_score's computation is now
	// covered by CountryScoreCollectorTests and the ordered-pipeline coverage in
	// ResourceSystemTests/InitSystemTests. See
	// Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md.
	public class CountryScoreSystemTests {
		[Fact]
		void get_score_returns_zero_for_unknown_country() {
			var world = new World();

			Assert.Equal(0.0, CountryScoreSystem.GetScore(world, "Unknown"));
		}
	}
}
