using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CardDrawOfferTests {
		sealed class CountingRandom : Random {
			public int Calls { get; private set; }

			public override int Next(int maxValue) {
				Calls++;
				return base.Next(maxValue);
			}
		}

		static ActionConfig BuildConfig() {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 8 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "a", OwnerType = "country", DeckCopies = 1 },
					new ActionDefinition { ActionId = "b", OwnerType = "country", DeckCopies = 2 },
					new ActionDefinition { ActionId = "c", OwnerType = "country", DeckCopies = 3 },
					new ActionDefinition { ActionId = "d", OwnerType = "country", DeckCopies = 0 },
					new ActionDefinition { ActionId = "make_rival", OwnerType = "country", DeckCopies = 1 },
					new ActionDefinition { ActionId = "stop_rivalry", OwnerType = "country", DeckCopies = 1 },
					new ActionDefinition { ActionId = "declare_war", OwnerType = "country", DeckCopies = 1 },
					new ActionDefinition { ActionId = "declare_revenge_war", OwnerType = "country", DeckCopies = 1 }
				}
			};
		}

		static int AddDeck(World world, string orgId, int handSize = 8) {
			int entity = world.Create();
			world.Add(entity, new CardDeck { OrgId = orgId });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(entity, new CardHand { HandSize = handSize });
			return entity;
		}

		static int AddCard(World world, string orgId, string actionId) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = orgId });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
			return entity;
		}

		static int AddCountry(World world, string countryId, bool destroyed = false) {
			int entity = world.Create();
			world.Add(entity, new Country { CountryId = countryId });
			if (destroyed) {
				world.Add(entity, new IsDestroyed());
			}
			return entity;
		}

		static int AddRelationTargetCard(
			World world,
			string orgId,
			string actionId,
			string? countryContextId,
			string targetCountryId,
			RelationKind kind) {
			int entity = AddCard(world, orgId, actionId);
			if (!string.IsNullOrEmpty(countryContextId)) {
				world.Add(entity, new CountryContext { CountryId = countryContextId });
			}
			world.Add(entity, new RelationCardTarget { TargetCountryId = targetCountryId, Kind = kind });
			return entity;
		}

		static int AddRevengeTargetCard(
			World world,
			string orgId,
			string countryContextId,
			string targetCountryId) {
			int entity = AddCard(world, orgId, "declare_revenge_war");
			world.Add(entity, new CountryContext { CountryId = countryContextId });
			world.Add(entity, new RevengeCardTarget { TargetCountryId = targetCountryId });
			return entity;
		}

		[Fact]
		void draw_command_creates_three_ordered_choices_without_adding_to_hand() {
			var world = new World();
			int deck = AddDeck(world, "OrgA");
			AddCard(world, "OrgA", "a");
			AddCard(world, "OrgA", "b");
			AddCard(world, "OrgA", "c");

			DrawCardSystem.Update(
				world,
				BuildConfig(),
				new Random(7),
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "OrgA" } }),
				Array.Empty<DiscardCardResult>());

			Assert.True(world.Has<PendingCardDraw>(deck));
			Assert.Equal(3, world.Get<PendingCardDraw>(deck).OptionCount);
			IReadOnlyList<CountryCardDrawChoiceInfo> choices = CountryCardDrawQuery.GetChoices(world, "OrgA");
			Assert.Equal(new[] { 0, 1, 2 }, choices.Select(choice => choice.ChoiceIndex));
			Assert.All(choices, choice => Assert.False(world.Has<CardInHand>(choice.Entity)));
		}

		[Fact]
		void receive_assigns_lowest_free_slot_and_clears_entire_offer() {
			var world = new World();
			int deck = AddDeck(world, "OrgA", handSize: 3);
			int occupied = AddCard(world, "OrgA", "a");
			world.Add(occupied, new CardInHand { SlotIndex = 1 });
			int first = AddCard(world, "OrgA", "b");
			int selected = AddCard(world, "OrgA", "c");
			world.Add(deck, new PendingCardDraw { OptionCount = 2 });
			world.Add(first, new CardDrawChoice { ChoiceIndex = 0 });
			world.Add(selected, new CardDrawChoice { ChoiceIndex = 1 });

			ReceiveCardSystem.Update(
				world,
				new ReadCommands<ReceiveCardCommand>(new[] { new ReceiveCardCommand { OrgId = "OrgA", ChoiceIndex = 1 } }));

			Assert.True(world.Has<CardInHand>(selected));
			Assert.Equal(0, world.Get<CardInHand>(selected).SlotIndex);
			Assert.False(world.Has<CardInHand>(first));
			Assert.False(world.Has<CardDrawChoice>(first));
			Assert.False(world.Has<CardDrawChoice>(selected));
			Assert.False(world.Has<PendingCardDraw>(deck));
		}

		[Fact]
		void rejected_full_hand_draw_does_not_consume_rng() {
			var world = new World();
			AddDeck(world, "OrgA", handSize: 1);
			int card = AddCard(world, "OrgA", "a");
			world.Add(card, new CardInHand { SlotIndex = 0 });
			AddCard(world, "OrgA", "b");
			var rng = new CountingRandom();

			DrawCardSystem.Update(
				world,
				BuildConfig(),
				rng,
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "OrgA" } }),
				Array.Empty<DiscardCardResult>());

			Assert.Equal(0, rng.Calls);
			Assert.Empty(CountryCardDrawQuery.GetChoices(world, "OrgA"));
		}

		[Fact]
		void orphan_choice_marker_rejects_draw_without_consuming_rng() {
			var world = new World();
			AddDeck(world, "OrgA");
			int orphan = AddCard(world, "OrgA", "a");
			world.Add(orphan, new CardDrawChoice { ChoiceIndex = 0 });
			AddCard(world, "OrgA", "b");
			var rng = new CountingRandom();

			Assert.True(CountryCardDrawQuery.TryGetStatus(
				world, BuildConfig(), "OrgA", out CountryCardDrawStatus status));
			Assert.True(status.HasOfferMarkers);
			Assert.False(status.HasCoherentPendingDraw);
			Assert.False(status.CanStartDraw);
			DrawCardSystem.Update(
				world,
				BuildConfig(),
				rng,
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "OrgA" } }),
				Array.Empty<DiscardCardResult>());

			Assert.Equal(0, rng.Calls);
			Assert.False(status.HasPendingDraw);
		}

		[Theory]
		[InlineData(0)]
		[InlineData(4)]
		void invalid_pending_option_count_is_incoherent_and_rejects_draw(int optionCount) {
			var world = new World();
			int deck = AddDeck(world, "OrgA");
			AddCard(world, "OrgA", "a");
			world.Add(deck, new PendingCardDraw { OptionCount = optionCount });
			var rng = new CountingRandom();

			Assert.True(CountryCardDrawQuery.TryGetStatus(
				world, BuildConfig(), "OrgA", out CountryCardDrawStatus status));
			Assert.True(status.HasPendingDraw);
			Assert.True(status.HasOfferMarkers);
			Assert.False(status.HasCoherentPendingDraw);
			Assert.False(status.CanStartDraw);
			DrawCardSystem.Update(
				world,
				BuildConfig(),
				rng,
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "OrgA" } }),
				Array.Empty<DiscardCardResult>());

			Assert.Equal(0, rng.Calls);
		}

		[Fact]
		void draw_ignores_zero_weight_hand_discard_and_foreign_cards() {
			var world = new World();
			AddDeck(world, "OrgA");
			int drawable = AddCard(world, "OrgA", "a");
			int zeroWeight = AddCard(world, "OrgA", "d");
			int inHand = AddCard(world, "OrgA", "b");
			world.Add(inHand, new CardInHand { SlotIndex = 0 });
			int discarded = AddCard(world, "OrgA", "c");
			world.Add(discarded, new CardDiscard());
			int foreign = AddCard(world, "OrgB", "c");

			DrawCardSystem.Update(
				world,
				BuildConfig(),
				new Random(1),
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "OrgA" } }),
				Array.Empty<DiscardCardResult>());

			CountryCardDrawChoiceInfo choice = Assert.Single(CountryCardDrawQuery.GetChoices(world, "OrgA"));
			Assert.Equal(drawable, choice.Entity);
			Assert.False(world.Has<CardDrawChoice>(zeroWeight));
			Assert.False(world.Has<CardDrawChoice>(inHand));
			Assert.False(world.Has<CardDrawChoice>(discarded));
			Assert.False(world.Has<CardDrawChoice>(foreign));
		}

		[Fact]
		void get_drawable_cards_excludes_country_targeted_cards_for_destroyed_countries() {
			var world = new World();
			AddDeck(world, "OrgA");
			AddCountry(world, "Prussia");
			AddCountry(world, "France", destroyed: true);
			AddCountry(world, "Austria");

			int makeRivalDead = AddRelationTargetCard(
				world, "OrgA", "make_rival", null, "France", RelationKind.Rival);
			int stopRivalryDead = AddRelationTargetCard(
				world, "OrgA", "stop_rivalry", "Prussia", "France", RelationKind.Rival);
			int declareWarDead = AddRelationTargetCard(
				world, "OrgA", "declare_war", "Prussia", "France", RelationKind.Rival);
			int revengeDead = AddRevengeTargetCard(world, "OrgA", "Prussia", "France");
			int homeDestroyed = AddRelationTargetCard(
				world, "OrgA", "stop_rivalry", "France", "Austria", RelationKind.Rival);

			int makeRivalLive = AddRelationTargetCard(
				world, "OrgA", "make_rival", null, "Austria", RelationKind.Rival);
			int ordinary = AddCard(world, "OrgA", "a");

			IReadOnlyList<CountryCardDrawCandidate> drawable = CountryCardDrawQuery.GetDrawableCards(
				world, BuildConfig(), "OrgA");
			var entities = new HashSet<int>(drawable.Select(candidate => candidate.Entity));

			Assert.DoesNotContain(makeRivalDead, entities);
			Assert.DoesNotContain(stopRivalryDead, entities);
			Assert.DoesNotContain(declareWarDead, entities);
			Assert.DoesNotContain(revengeDead, entities);
			Assert.DoesNotContain(homeDestroyed, entities);
			Assert.Contains(makeRivalLive, entities);
			Assert.Contains(ordinary, entities);
		}

		[Fact]
		void draw_offer_skips_destroyed_country_targets_and_leaves_hand_cards() {
			var world = new World();
			AddDeck(world, "OrgA");
			AddCountry(world, "Prussia");
			AddCountry(world, "France", destroyed: true);
			AddCountry(world, "Austria");

			int deadTarget = AddRelationTargetCard(
				world, "OrgA", "make_rival", null, "France", RelationKind.Rival);
			int liveTarget = AddRelationTargetCard(
				world, "OrgA", "make_rival", null, "Austria", RelationKind.Rival);
			int handCard = AddRelationTargetCard(
				world, "OrgA", "declare_war", "Prussia", "France", RelationKind.Rival);
			world.Add(handCard, new CardInHand { SlotIndex = 0 });

			DrawCardSystem.Update(
				world,
				BuildConfig(),
				new Random(1),
				new ReadCommands<DrawCardsCommand>(new[] { new DrawCardsCommand { OrgId = "OrgA" } }),
				Array.Empty<DiscardCardResult>());

			CountryCardDrawChoiceInfo choice = Assert.Single(CountryCardDrawQuery.GetChoices(world, "OrgA"));
			Assert.Equal(liveTarget, choice.Entity);
			Assert.False(world.Has<CardDrawChoice>(deadTarget));
			Assert.True(world.Has<CardInHand>(handCard));
			Assert.False(world.Has<CardDrawChoice>(handCard));
		}

		[Fact]
		void paid_discard_result_creates_offer_without_discarded_card() {
			var world = new World();
			AddDeck(world, "OrgA");
			int discarded = AddCard(world, "OrgA", "a");
			world.Add(discarded, new CardDiscard());
			int candidate = AddCard(world, "OrgA", "b");
			var discardCommand = new DiscardCardCommand { OrgId = "OrgA" };
			var results = new[] { new DiscardCardResult(discardCommand, DiscardCardOutcome.Succeeded) };

			DrawCardSystem.Update(
				world,
				BuildConfig(),
				new Random(1),
				new ReadCommands<DrawCardsCommand>(Array.Empty<DrawCardsCommand>()),
				results);

			CountryCardDrawChoiceInfo choice = Assert.Single(CountryCardDrawQuery.GetChoices(world, "OrgA"));
			Assert.Equal(candidate, choice.Entity);
		}

		[Fact]
		void offers_are_isolated_by_organization() {
			var world = new World();
			AddDeck(world, "OrgA");
			AddDeck(world, "OrgB");
			int cardA = AddCard(world, "OrgA", "a");
			int cardB = AddCard(world, "OrgB", "b");

			DrawCardSystem.Update(
				world,
				BuildConfig(),
				new Random(1),
				new ReadCommands<DrawCardsCommand>(new[] {
					new DrawCardsCommand { OrgId = "OrgA" },
					new DrawCardsCommand { OrgId = "OrgB" }
				}),
				Array.Empty<DiscardCardResult>());

			Assert.Equal(cardA, Assert.Single(CountryCardDrawQuery.GetChoices(world, "OrgA")).Entity);
			Assert.Equal(cardB, Assert.Single(CountryCardDrawQuery.GetChoices(world, "OrgB")).Entity);
		}

		[Fact]
		void pending_offer_round_trip_preserves_order_and_can_be_received() {
			var original = new World();
			int deck = AddDeck(original, "OrgA");
			int first = AddCard(original, "OrgA", "a");
			int second = AddCard(original, "OrgA", "b");
			original.Add(first, new CardDrawChoice { ChoiceIndex = 0 });
			original.Add(second, new CardDrawChoice { ChoiceIndex = 1 });
			original.Add(deck, new PendingCardDraw { OptionCount = 2 });

			var restored = new World();
			LoadSystem.Apply(SaveSystem.BuildSnapshot(original), restored);

			Assert.Equal(new[] { 0, 1 }, CountryCardDrawQuery.GetChoices(restored, "OrgA").Select(choice => choice.ChoiceIndex));
			ReceiveCardSystem.Update(
				restored,
				new ReadCommands<ReceiveCardCommand>(new[] { new ReceiveCardCommand { OrgId = "OrgA", ChoiceIndex = 1 } }));
			Assert.Single(GetHandEntities(restored, "OrgA"));
			Assert.Empty(CountryCardDrawQuery.GetChoices(restored, "OrgA"));
		}

		[Fact]
		void receive_rejects_offer_when_total_hand_count_already_equals_cap_with_duplicate_slots() {
			var world = new World();
			int deck = AddDeck(world, "OrgA", handSize: 8);
			for (int i = 0; i < 8; i++) {
				int handCard = AddCard(world, "OrgA", i % 2 == 0 ? "a" : "b");
				world.Add(handCard, new CardInHand { SlotIndex = i % 2 == 0 ? 0 : 99 });
			}
			int choice = AddCard(world, "OrgA", "c");
			world.Add(choice, new CardDrawChoice { ChoiceIndex = 0 });
			world.Add(deck, new PendingCardDraw { OptionCount = 1 });

			ReceiveCardSystem.Update(
				world,
				new ReadCommands<ReceiveCardCommand>(new[] {
					new ReceiveCardCommand { OrgId = "OrgA", ChoiceIndex = 0 }
				}));

			Assert.Equal(8, GetHandEntities(world, "OrgA").Count);
			Assert.False(world.Has<CardInHand>(choice));
			Assert.True(world.Has<CardDrawChoice>(choice));
			Assert.True(world.Has<PendingCardDraw>(deck));
		}

		[Fact]
		void receive_rejects_option_count_above_three() {
			var world = new World();
			int deck = AddDeck(world, "OrgA");
			for (int i = 0; i < 4; i++) {
				int choice = AddCard(world, "OrgA", i == 0 ? "a" : "b");
				world.Add(choice, new CardDrawChoice { ChoiceIndex = i });
			}
			world.Add(deck, new PendingCardDraw { OptionCount = 4 });

			ReceiveCardSystem.Update(
				world,
				new ReadCommands<ReceiveCardCommand>(new[] {
					new ReceiveCardCommand { OrgId = "OrgA", ChoiceIndex = 0 }
				}));

			Assert.Empty(GetHandEntities(world, "OrgA"));
			Assert.Equal(4, CountryCardDrawQuery.GetChoices(world, "OrgA").Count);
			Assert.True(world.Has<PendingCardDraw>(deck));
		}

		static List<int> GetHandEntities(IReadOnlyWorld world, string orgId) {
			var entities = new List<int>();
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId == orgId && owners[i].Value == CardOwnerKind.Country) {
						entities.Add(arch.Entities[i]);
					}
				}
			}
			return entities;
		}
	}
}
