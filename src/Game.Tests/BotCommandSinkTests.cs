using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotCommandSinkTests {
		sealed class CapturingCommandAccessor : IWriteOnlyCommandAccessor {
			public List<ICommand> Commands = new();
			public void Push<TCommand>(TCommand cmd) where TCommand : ICommand => Commands.Add(cmd);
		}

		sealed class CapturingLogger : IGameLogger {
			public List<string> Infos = new();
			public void LogError(string message) { }
			public void LogInfo(string message) => Infos.Add(message);
			public void LogDebug(string message) { }
		}

		static List<(string ActionId, int SlotIndex)> GetHandContents(GameLogic logic, string orgId) {
			var result = new List<(string, int)>();
			int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
			int[] exclude = { TypeId<CountryContext>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, exclude)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId) { result.Add((actions[i].ActionId, hands[i].SlotIndex)); }
				}
			}
			result.Sort((a, b) => a.Item1 == b.Item1 ? a.Item2.CompareTo(b.Item2) : string.CompareOrdinal(a.Item1, b.Item1));
			return result;
		}

		static int GetSlot(GameLogic logic, string orgId, CardOwnerKind ownerKind, string actionId) {
			int[] req = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId && owners[i].Value == ownerKind && actions[i].ActionId == actionId) {
						return hands[i].SlotIndex;
					}
				}
			}
			throw new KeyNotFoundException($"Hand card not found: org={orgId} owner={ownerKind} action={actionId}");
		}

		[Fact]
		void sink_stamps_org_id_on_all_commands() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 25);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			double goldABefore = OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA);
			double goldBBefore = OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgB);

			var sinkA = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);
			sinkA.BeginDecisionPhase();
			sinkA.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId,
				GetSlot(logic, MultiOrgTestSupport.OrgA, CardOwnerKind.Org, MultiOrgTestSupport.SpendGoldActionId));
			logic.Update(0f);

			Assert.Equal(goldABefore - 50.0, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA));
			Assert.Equal(goldBBefore, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgB));
		}

		[Fact]
		void bot_emitted_play_produces_identical_outcome_to_direct_push() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };

			var ctxSink = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 21);
			var logicSink = new GameLogic(ctxSink);
			logicSink.Update(0f);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logicSink.Commands, null);
			sink.BeginDecisionPhase();
			int spendSlot = GetSlot(logicSink, MultiOrgTestSupport.OrgA, CardOwnerKind.Org, MultiOrgTestSupport.SpendGoldActionId);
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId, spendSlot);
			logicSink.Update(0f);

			var ctxDirect = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 21);
			var logicDirect = new GameLogic(ctxDirect);
			logicDirect.Update(0f);
			logicDirect.Commands.Push(new PlayCardActionCommand {
				OrgId = MultiOrgTestSupport.OrgA,
				ActionId = MultiOrgTestSupport.SpendGoldActionId,
				CountryId = "",
				SlotIndex = GetSlot(logicDirect, MultiOrgTestSupport.OrgA, CardOwnerKind.Org, MultiOrgTestSupport.SpendGoldActionId)
			});
			logicDirect.Update(0f);

			Assert.Equal(OrgMetrics.GetGold(logicDirect.World, MultiOrgTestSupport.OrgA), OrgMetrics.GetGold(logicSink.World, MultiOrgTestSupport.OrgA));
			Assert.Equal(OrgMetrics.GetTotalControl(logicDirect.World, MultiOrgTestSupport.OrgA), OrgMetrics.GetTotalControl(logicSink.World, MultiOrgTestSupport.OrgA));
			Assert.Equal(OrgMetrics.GetControlByCountry(logicDirect.World, MultiOrgTestSupport.OrgA), OrgMetrics.GetControlByCountry(logicSink.World, MultiOrgTestSupport.OrgA));
			Assert.Equal(GetHandContents(logicDirect, MultiOrgTestSupport.OrgA), GetHandContents(logicSink, MultiOrgTestSupport.OrgA));
		}

		[Fact]
		void duplicate_play_in_same_phase_is_ignored_and_logged() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 22);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var logger = new CapturingLogger();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, logger);

			double goldBefore = OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA);
			sink.BeginDecisionPhase();
			int spendSlot = GetSlot(logic, MultiOrgTestSupport.OrgA, CardOwnerKind.Org, MultiOrgTestSupport.SpendGoldActionId);
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId, spendSlot);
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId, spendSlot);
			logic.Update(0f);

			Assert.Equal(goldBefore - 50.0, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA));
			Assert.Contains(logger.Infos, m => m.Contains("duplicate"));
		}

		[Fact]
		void distinct_plays_in_same_phase_are_all_emitted() {
			var commands = new CapturingCommandAccessor();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, null);

			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId, 0);
			sink.PlayCountryCard(MultiOrgTestSupport.CountryCardActionId, MultiOrgTestSupport.HqA, 1, "");

			Assert.Equal(2, commands.Commands.OfType<PlayCardActionCommand>().Count());
		}

		[Fact]
		void begin_decision_phase_resets_duplicate_guard() {
			var commands = new CapturingCommandAccessor();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, null);

			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId, 0);
			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId, 0);

			Assert.Equal(2, commands.Commands.OfType<PlayCardActionCommand>().Count());
		}

		[Fact]
		void acquisition_commands_are_org_stamped_and_do_not_emit_play_callbacks() {
			var commands = new CapturingCommandAccessor();
			var emissions = new List<(string ActionId, string CountryId)>();
			var sink = new BotCommandSink(
				MultiOrgTestSupport.OrgA,
				commands,
				null,
				(actionId, countryId) => emissions.Add((actionId, countryId)));

			sink.BeginDecisionPhase();
			sink.DrawCountryCards();
			sink.ReceiveCountryCard(2);

			Assert.Collection(
				commands.Commands,
				command => Assert.Equal(MultiOrgTestSupport.OrgA, Assert.IsType<DrawCardsCommand>(command).OrgId),
				command => {
					var receive = Assert.IsType<ReceiveCardCommand>(command);
					Assert.Equal(MultiOrgTestSupport.OrgA, receive.OrgId);
					Assert.Equal(2, receive.ChoiceIndex);
				});
			Assert.Empty(emissions);
		}

		[Fact]
		void duplicate_acquisition_commands_in_same_phase_are_ignored_and_logged() {
			var commands = new CapturingCommandAccessor();
			var logger = new CapturingLogger();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, logger);

			sink.BeginDecisionPhase();
			sink.DrawCountryCards();
			sink.DrawCountryCards();
			sink.ReceiveCountryCard(1);
			sink.ReceiveCountryCard(1);

			Assert.Equal(2, commands.Commands.Count);
			Assert.Single(commands.Commands.OfType<DrawCardsCommand>());
			Assert.Single(commands.Commands.OfType<ReceiveCardCommand>());
			Assert.Equal(2, logger.Infos.Count(m => m.Contains("duplicate")));
		}

		[Fact]
		void distinct_receive_choices_in_same_phase_are_not_duplicates() {
			var commands = new CapturingCommandAccessor();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, null);

			sink.BeginDecisionPhase();
			sink.ReceiveCountryCard(0);
			sink.ReceiveCountryCard(1);

			Assert.Equal(new[] { 0, 1 }, commands.Commands.OfType<ReceiveCardCommand>().Select(command => command.ChoiceIndex));
		}

		[Fact]
		void begin_decision_phase_resets_acquisition_duplicate_guards() {
			var commands = new CapturingCommandAccessor();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, null);

			sink.BeginDecisionPhase();
			sink.DrawCountryCards();
			sink.ReceiveCountryCard(0);
			sink.BeginDecisionPhase();
			sink.DrawCountryCards();
			sink.ReceiveCountryCard(0);

			Assert.Equal(2, commands.Commands.OfType<DrawCardsCommand>().Count());
			Assert.Equal(2, commands.Commands.OfType<ReceiveCardCommand>().Count());
		}

		[Fact]
		void duplicate_discard_in_same_phase_is_ignored() {
			var commands = new CapturingCommandAccessor();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, null);

			sink.BeginDecisionPhase();
			sink.DiscardCountryCard("card_a", "Austria", 0, "");
			sink.DiscardCountryCard("card_b", "Austria", 1, "");

			Assert.Single(commands.Commands.OfType<DiscardCardCommand>());
		}

		[Fact]
		void begin_decision_phase_resets_discard_duplicate_guard() {
			var commands = new CapturingCommandAccessor();
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, commands, null);

			sink.BeginDecisionPhase();
			sink.DiscardCountryCard("card_a", "Austria", 0, "");
			sink.BeginDecisionPhase();
			sink.DiscardCountryCard("card_a", "Austria", 0, "");

			Assert.Equal(2, commands.Commands.OfType<DiscardCardCommand>().Count());
		}

		[Fact]
		void sink_interface_exposes_only_whitelisted_members() {
			var methods = typeof(IBotCommandSink).GetMethods();
			Assert.Equal(5, methods.Length);
			Assert.Contains(methods, m =>
				m.Name == "DrawCountryCards" && !m.IsGenericMethod && m.GetParameters().Length == 0);
			Assert.Contains(methods, m =>
				m.Name == "ReceiveCountryCard" && !m.IsGenericMethod &&
				m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));
			Assert.Contains(methods, m =>
				m.Name == "PlayOrgCard" && !m.IsGenericMethod &&
				m.GetParameters().Length == 2
				&& m.GetParameters()[0].ParameterType == typeof(string)
				&& m.GetParameters()[1].ParameterType == typeof(int));
			Assert.Contains(methods, m =>
				m.Name == "PlayCountryCard" && !m.IsGenericMethod &&
				m.GetParameters().Length == 4
				&& m.GetParameters()[0].ParameterType == typeof(string)
				&& m.GetParameters()[1].ParameterType == typeof(string)
				&& m.GetParameters()[2].ParameterType == typeof(int)
				&& m.GetParameters()[3].ParameterType == typeof(string));
			Assert.Contains(methods, m =>
				m.Name == "DiscardCountryCard" && !m.IsGenericMethod &&
				m.GetParameters().Length == 4
				&& m.GetParameters()[0].ParameterType == typeof(string)
				&& m.GetParameters()[1].ParameterType == typeof(string)
				&& m.GetParameters()[2].ParameterType == typeof(int)
				&& m.GetParameters()[3].ParameterType == typeof(string));
		}
	}
}
