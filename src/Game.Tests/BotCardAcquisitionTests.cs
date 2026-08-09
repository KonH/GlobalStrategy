using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotCardAcquisitionTests {
		const string OrgId = "Illuminati";
		const string CountryId = "Austria";
		const string ControlUsableActionId = "control_usable";
		const string ControlActionId = "control";
		const string PlayableActionId = "playable";
		const string FallbackActionId = "fallback";

		sealed class CapturingCommandAccessor : IWriteOnlyCommandAccessor {
			public List<ICommand> Commands = new();
			public void Push<TCommand>(TCommand cmd) where TCommand : ICommand => Commands.Add(cmd);
		}

		sealed class ScriptedPlayFeature : IBotFeature {
			public int TickCount;
			public string FeatureId => "scripted";

			public void Tick(IBotObservation observation, IBotCommandSink sink, Random rng) {
				TickCount++;
				sink.PlayOrgCard("scripted_play", 0);
			}
		}

		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 8 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = ControlUsableActionId,
						OwnerType = "country",
						DeckCopies = 1,
						EffectIds = new List<string> { "control_up" }
					},
					new ActionDefinition {
						ActionId = ControlActionId,
						OwnerType = "country",
						DeckCopies = 1,
						EffectIds = new List<string> { "control_up" },
						Cost = new List<ActionCost> { new ActionCost { ResourceId = ResourceDefinitions.Gold, Amount = 1 } }
					},
					new ActionDefinition {
						ActionId = PlayableActionId,
						OwnerType = "country",
						DeckCopies = 1
					},
					new ActionDefinition {
						ActionId = FallbackActionId,
						OwnerType = "country",
						DeckCopies = 1,
						Cost = new List<ActionCost> { new ActionCost { ResourceId = ResourceDefinitions.Gold, Amount = 1 } }
					}
				}
			};
		}

		static EffectConfig BuildEffectConfig() {
			return new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams {
						EffectId = "control_up",
						EffectType = "ControlChange",
						Amount = 5
					}
				}
			};
		}

		static (World World, int DeckEntity) BuildWorld(int handSize = 8) {
			var world = new World();
			int timeEntity = world.Create();
			world.Add(timeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1) });
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country(CountryId));
			int deckEntity = world.Create();
			world.Add(deckEntity, new CardDeck { OrgId = OrgId });
			world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(deckEntity, new CardHand { HandSize = handSize });
			return (world, deckEntity);
		}

		static int AddCountryCard(World world, string actionId, int? choiceIndex = null) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = actionId });
			world.Add(entity, new OrgContext { OrgId = OrgId });
			world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
			if (choiceIndex.HasValue) {
				world.Add(entity, new CardDrawChoice { ChoiceIndex = choiceIndex.Value });
			}
			return entity;
		}

		static Bot BuildBot(
			World world,
			CapturingCommandAccessor commands,
			IReadOnlyList<IBotFeature>? features = null) {
			var sink = new BotCommandSink(OrgId, commands, null);
			return new Bot(
				OrgId,
				features ?? Array.Empty<IBotFeature>(),
				new Random(1),
				sink,
				new ResourceQuery(),
				new CountryRelations(),
				BuildEffectConfig());
		}

		[Fact]
		void bot_requests_draw_without_running_play_features_in_same_step() {
			var (world, _) = BuildWorld();
			AddCountryCard(world, PlayableActionId);
			var commands = new CapturingCommandAccessor();
			var feature = new ScriptedPlayFeature();
			Bot bot = BuildBot(world, commands, new[] { feature });

			bot.ExecuteDecisionTick(world, BuildActionConfig());

			DrawCardsCommand draw = Assert.IsType<DrawCardsCommand>(Assert.Single(commands.Commands));
			Assert.Equal(OrgId, draw.OrgId);
			Assert.Equal(0, feature.TickCount);
		}

		[Fact]
		void acquisition_is_serviced_before_same_day_play_gate() {
			var (world, deckEntity) = BuildWorld();
			var commands = new CapturingCommandAccessor();
			var feature = new ScriptedPlayFeature();
			Bot bot = BuildBot(world, commands, new[] { feature });
			ActionConfig actionConfig = BuildActionConfig();

			bot.ExecuteDecisionTick(world, actionConfig);
			Assert.IsType<PlayCardActionCommand>(Assert.Single(commands.Commands));
			Assert.Equal(1, feature.TickCount);

			AddCountryCard(world, PlayableActionId, choiceIndex: 0);
			world.Add(deckEntity, new PendingCardDraw { OptionCount = 1 });
			bot.ExecuteDecisionTick(world, actionConfig);

			Assert.Equal(2, commands.Commands.Count);
			ReceiveCardCommand receive = Assert.IsType<ReceiveCardCommand>(commands.Commands[1]);
			Assert.Equal(0, receive.ChoiceIndex);
			Assert.Equal(1, feature.TickCount);
		}

		[Theory]
		[InlineData(true, true, true, 2)]
		[InlineData(false, true, true, 2)]
		[InlineData(false, false, true, 2)]
		[InlineData(false, false, false, 0)]
		void bot_selects_control_usable_then_control_then_playable_then_lowest_index(
			bool includeControlUsable,
			bool includeControl,
			bool includePlayable,
			int expectedChoiceIndex) {
			var (world, deckEntity) = BuildWorld();
			AddCountryCard(world, FallbackActionId, choiceIndex: 0);
			if (includeControlUsable) {
				AddCountryCard(world, ControlActionId, choiceIndex: 1);
				AddCountryCard(world, ControlUsableActionId, choiceIndex: 2);
			} else if (includeControl) {
				AddCountryCard(world, PlayableActionId, choiceIndex: 1);
				AddCountryCard(world, ControlActionId, choiceIndex: 2);
			} else if (includePlayable) {
				AddCountryCard(world, FallbackActionId, choiceIndex: 1);
				AddCountryCard(world, PlayableActionId, choiceIndex: 2);
			} else {
				AddCountryCard(world, FallbackActionId, choiceIndex: 1);
				AddCountryCard(world, FallbackActionId, choiceIndex: 2);
			}
			world.Add(deckEntity, new PendingCardDraw { OptionCount = 3 });
			var commands = new CapturingCommandAccessor();
			Bot bot = BuildBot(world, commands);

			bot.ExecuteDecisionTick(world, BuildActionConfig());

			ReceiveCardCommand receive = Assert.IsType<ReceiveCardCommand>(Assert.Single(commands.Commands));
			Assert.Equal(expectedChoiceIndex, receive.ChoiceIndex);
		}

		[Fact]
		void observation_exposes_authoritative_hand_and_ordered_choice_metadata() {
			var (world, deckEntity) = BuildWorld(handSize: 8);
			int inHand = AddCountryCard(world, PlayableActionId);
			world.Add(inHand, new CardInHand { SlotIndex = 0 });
			AddCountryCard(world, FallbackActionId, choiceIndex: 1);
			AddCountryCard(world, ControlUsableActionId, choiceIndex: 0);
			world.Add(deckEntity, new PendingCardDraw { OptionCount = 2 });

			BotObservation observation = BotObservation.Build(
				world,
				BuildActionConfig(),
				OrgId,
				new ResourceQuery(),
				new CountryRelations(),
				BuildEffectConfig());

			Assert.Equal(1, observation.CountryHandCount);
			Assert.Equal(8, observation.CountryHandCapacity);
			Assert.False(observation.CanStartCountryCardDraw);
			Assert.Equal(new[] { 0, 1 }, observation.CountryCardDrawChoices.Select(choice => choice.ChoiceIndex));
			BotCardDrawChoiceView controlChoice = observation.CountryCardDrawChoices[0];
			Assert.Equal(ControlUsableActionId, controlChoice.ActionId);
			Assert.True(controlChoice.RaisesControl);
			Assert.True(controlChoice.IsPlayable);
			Assert.True(controlChoice.IsControlUsable);
			BotCardDrawChoiceView fallbackChoice = observation.CountryCardDrawChoices[1];
			Assert.False(fallbackChoice.RaisesControl);
			Assert.False(fallbackChoice.IsPlayable);
			Assert.False(fallbackChoice.IsControlUsable);
			Assert.Equal(1, fallbackChoice.GoldCost);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		void observation_hides_orphan_and_malformed_draw_choice_markers(bool addMismatchedPendingDraw) {
			var (world, deckEntity) = BuildWorld();
			AddCountryCard(world, ControlUsableActionId, choiceIndex: 0);
			if (addMismatchedPendingDraw) {
				world.Add(deckEntity, new PendingCardDraw { OptionCount = 2 });
			}

			BotObservation observation = BotObservation.Build(
				world,
				BuildActionConfig(),
				OrgId,
				new ResourceQuery(),
				new CountryRelations(),
				BuildEffectConfig());

			Assert.Empty(observation.CountryCardDrawChoices);
		}
	}
}
