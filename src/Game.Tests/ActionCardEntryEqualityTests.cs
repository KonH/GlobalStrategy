using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class ActionCardEntryEqualityTests {
		[Fact]
		void differing_war_win_chance_percent_is_not_equal() {
			var a = new ActionCardEntry("declare_war", 0, true, targetCountryId: "France", warWinChancePercent: 40);
			var b = new ActionCardEntry("declare_war", 0, true, targetCountryId: "France", warWinChancePercent: 55);

			Assert.False(StateEquality.ActionCardEntryEquals(a, b));
		}

		[Fact]
		void matching_war_win_chance_percent_is_equal() {
			var a = new ActionCardEntry("declare_war", 0, true, targetCountryId: "France", warWinChancePercent: 50);
			var b = new ActionCardEntry("declare_war", 0, true, targetCountryId: "France", warWinChancePercent: 50);

			Assert.True(StateEquality.ActionCardEntryEquals(a, b));
		}

		[Fact]
		void null_vs_value_war_win_chance_is_not_equal() {
			var a = new ActionCardEntry("make_friend", 0, true);
			var b = new ActionCardEntry("make_friend", 0, true, warWinChancePercent: 50);

			Assert.False(StateEquality.ActionCardEntryEquals(a, b));
		}
	}
}
