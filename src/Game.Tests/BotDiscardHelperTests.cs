using System.Collections.Generic;
using GS.Game.Bots;
using Xunit;

namespace GS.Game.Tests {
	public class BotDiscardHelperTests {
		sealed class RecordingSink : IBotCommandSink {
			public List<(string ActionId, int SlotIndex, string CountryId)> Discards = new();
			public void DrawCountryCards() { }
			public void ReceiveCountryCard(int choiceIndex) { }
			public void PlayOrgCard(string actionId, int slotIndex) { }
			public void PlayCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId) { }
			public void DiscardCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId) {
				Discards.Add((actionId, slotIndex, countryId));
			}
		}

		sealed class FakeObservation : IBotObservation {
			public string OrgId => "Illuminati";
			public System.DateTime CurrentDate => default;
			public double Gold { get; set; } = 1000;
			public double OrgScore => 0;
			public IReadOnlyList<BotOrgScoreView> OrgScores => System.Array.Empty<BotOrgScoreView>();
			public int OrgHandSize => 0;
			public int CountryHandCount { get; set; }
			public int CountryHandCapacity { get; set; }
			public bool CanStartCountryCardDraw => false;
			public int TotalControl => 0;
			public IReadOnlyList<BotCardView> OrgHand => System.Array.Empty<BotCardView>();
			public IReadOnlyList<BotCardDrawChoiceView> CountryCardDrawChoices => System.Array.Empty<BotCardDrawChoiceView>();
			public IReadOnlyList<BotCharacterSlotView> CharacterSlots => System.Array.Empty<BotCharacterSlotView>();
			public IReadOnlyList<BotCountryView> Countries { get; set; } = System.Array.Empty<BotCountryView>();
			public BotCountryView? GetCountry(string countryId) => null;
		}

		[Fact]
		void full_hand_discards_lowest_value_then_lowest_slot_index() {
			var obs = new FakeObservation {
				CountryHandCount = 2,
				CountryHandCapacity = 2,
				Countries = new[] {
					new BotCountryView {
						CountryId = "Austria",
						Hand = new[] {
							new BotCardView { ActionId = "raise_control", SlotIndex = 1, CountryId = "Austria", RaisesControl = true, IsPlayable = true },
							new BotCardView { ActionId = "junk", SlotIndex = 0, CountryId = "Austria", IsPlayable = false }
						}
					}
				}
			};
			var sink = new RecordingSink();

			Assert.True(BotDiscardHelper.TryDiscardForBetterHand(obs, sink));
			Assert.Single(sink.Discards);
			Assert.Equal(("junk", 0, "Austria"), sink.Discards[0]);
		}

		[Fact]
		void non_full_hand_does_not_discard() {
			var obs = new FakeObservation {
				CountryHandCount = 1,
				CountryHandCapacity = 2,
				Countries = new[] {
					new BotCountryView {
						CountryId = "Austria",
						Hand = new[] {
							new BotCardView { ActionId = "junk", SlotIndex = 0, CountryId = "Austria" }
						}
					}
				}
			};
			var sink = new RecordingSink();

			Assert.False(BotDiscardHelper.TryDiscardForBetterHand(obs, sink));
			Assert.Empty(sink.Discards);
		}

		[Fact]
		void duplicate_per_country_views_collapse_to_lowest_slot() {
			var shared = new BotCardView { ActionId = "opinion", SlotIndex = 3, CountryId = "Austria", IsPlayable = true };
			var obs = new FakeObservation {
				CountryHandCount = 1,
				CountryHandCapacity = 1,
				Countries = new[] {
					new BotCountryView {
						CountryId = "Austria",
						Hand = new[] { shared, new BotCardView { ActionId = "opinion", SlotIndex = 3, CountryId = "Austria", IsPlayable = true } }
					},
					new BotCountryView {
						CountryId = "Prussia",
						Hand = new[] { new BotCardView { ActionId = "opinion", SlotIndex = 3, CountryId = "Prussia", IsPlayable = true } }
					}
				}
			};
			var sink = new RecordingSink();

			Assert.True(BotDiscardHelper.TryDiscardForBetterHand(obs, sink));
			Assert.Single(sink.Discards);
			Assert.Equal(3, sink.Discards[0].SlotIndex);
		}

		[Fact]
		void unaffordable_discard_is_blocked_by_gold_gate() {
			var obs = new FakeObservation {
				Gold = 10,
				CountryHandCount = 1,
				CountryHandCapacity = 1,
				Countries = new[] {
					new BotCountryView {
						CountryId = "Austria",
						Hand = new[] { new BotCardView { ActionId = "junk", SlotIndex = 0, CountryId = "Austria" } }
					}
				}
			};
			var sink = new RecordingSink();

			Assert.False(BotDiscardHelper.TryDiscardForBetterHand(obs, sink, discardGoldCost: 50));
			Assert.Empty(sink.Discards);
		}
	}
}
