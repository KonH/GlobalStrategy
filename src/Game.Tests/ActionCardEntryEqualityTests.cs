using System.Collections.Generic;
using GS.Game.Configs;
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

		[Fact]
		void differing_playable_country_lists_are_not_equal() {
			var a = new ActionCardEntry(
				"improve_control", 0, true, playableCountryIds: new[] { "Austria", "Prussia" });
			var b = new ActionCardEntry(
				"improve_control", 0, true, playableCountryIds: new[] { "Austria", "France" });

			Assert.False(StateEquality.ActionCardEntryEquals(a, b));
		}

		[Fact]
		void differing_presentation_metadata_is_not_equal() {
			var aCondition = new ActionConditionDebugEntry(
				"control >= 10", false, "action.requirement.control_min", new[] { "10", "5" }, "insufficient_control");
			var bCondition = new ActionConditionDebugEntry(
				"control >= 10", false, "action.requirement.opinion_min", new[] { "10", "5" }, "insufficient_control");
			var a = new ActionCardEntry(
				"improve_control", 0, true, conditions: new List<ActionConditionDebugEntry> { aCondition }, firstFailure: aCondition);
			var b = new ActionCardEntry(
				"improve_control", 0, true, conditions: new List<ActionConditionDebugEntry> { bCondition }, firstFailure: bCondition);

			Assert.False(StateEquality.ActionCardEntryEquals(a, b));
		}

		[Fact]
		void differing_primary_country_context_is_not_equal() {
			var a = new ActionCardEntry(
				"make_friend", 0, true, countryContextId: "Prussia", targetCountryId: "Austria");
			var b = new ActionCardEntry(
				"make_friend", 0, true, countryContextId: "France", targetCountryId: "Austria");

			Assert.False(StateEquality.ActionCardEntryEquals(a, b));
		}

		[Fact]
		void draw_choice_equality_includes_choice_index_and_card() {
			var card = new ActionCardEntry("make_friend", -1, false, countryContextId: "Prussia");
			var equalCard = new ActionCardEntry("make_friend", -1, false, countryContextId: "Prussia");
			var differentCard = new ActionCardEntry("make_friend", -1, false, countryContextId: "France");

			Assert.True(StateEquality.CardDrawChoiceEntryEquals(
				new CardDrawChoiceEntry(0, card),
				new CardDrawChoiceEntry(0, equalCard)));
			Assert.False(StateEquality.CardDrawChoiceEntryEquals(
				new CardDrawChoiceEntry(0, card),
				new CardDrawChoiceEntry(1, equalCard)));
			Assert.False(StateEquality.CardDrawChoiceEntryEquals(
				new CardDrawChoiceEntry(0, card),
				new CardDrawChoiceEntry(0, differentCard)));
		}
	}
}
