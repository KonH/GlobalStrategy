using GS.Game.Bots;
using Xunit;

namespace GS.Game.Tests {
	public class BotDrawIntentScorerTests {
		static BotCardDrawChoiceView Choice(
			int index,
			string actionId,
			bool controlUsable = false,
			bool raisesControl = false,
			bool playable = false) {
			return new BotCardDrawChoiceView {
				ChoiceIndex = index,
				ActionId = actionId,
				IsControlUsable = controlUsable,
				RaisesControl = raisesControl,
				IsPlayable = playable
			};
		}

		sealed class EmptyObs : IBotObservation {
			public string OrgId => "";
			public System.DateTime CurrentDate => default;
			public double Gold => 0;
			public double OrgScore => 0;
			public System.Collections.Generic.IReadOnlyList<BotOrgScoreView> OrgScores => System.Array.Empty<BotOrgScoreView>();
			public int OrgHandSize => 0;
			public int CountryHandCount => 0;
			public int CountryHandCapacity => 0;
			public bool CanStartCountryCardDraw => false;
			public int TotalControl => 0;
			public System.Collections.Generic.IReadOnlyList<BotCardView> OrgHand => System.Array.Empty<BotCardView>();
			public System.Collections.Generic.IReadOnlyList<BotCardDrawChoiceView> CountryCardDrawChoices => System.Array.Empty<BotCardDrawChoiceView>();
			public System.Collections.Generic.IReadOnlyList<BotCharacterSlotView> CharacterSlots => System.Array.Empty<BotCharacterSlotView>();
			public System.Collections.Generic.IReadOnlyList<BotCountryView> Countries => System.Array.Empty<BotCountryView>();
			public BotCountryView? GetCountry(string countryId) => null;
		}

		[Fact]
		void control_usable_outranks_raises_control() {
			var obs = new EmptyObs();
			Assert.True(
				BotDrawIntentScorer.ScoreChoice(Choice(0, "a", controlUsable: true, raisesControl: true, playable: true), obs)
				> BotDrawIntentScorer.ScoreChoice(Choice(1, "b", raisesControl: true, playable: true), obs));
		}

		[Theory]
		[InlineData("declare_war")]
		[InlineData("sell_arms")]
		[InlineData("make_rival")]
		[InlineData("improve_diplomacy_advisor_opinion")]
		void war_intent_outranks_generic_playable(string actionId) {
			var obs = new EmptyObs();
			Assert.True(
				BotDrawIntentScorer.ScoreChoice(Choice(1, actionId, playable: true), obs)
				> BotDrawIntentScorer.ScoreChoice(Choice(0, "generic", playable: true), obs));
		}

		[Fact]
		void equal_band_prefers_lower_choice_index_via_bot_selection_contract() {
			var a = Choice(2, "declare_war", playable: true);
			var b = Choice(0, "sell_arms", playable: true);
			var obs = new EmptyObs();
			Assert.Equal(
				BotDrawIntentScorer.ScoreChoice(a, obs),
				BotDrawIntentScorer.ScoreChoice(b, obs));
			Assert.True(b.ChoiceIndex < a.ChoiceIndex);
		}
	}
}
