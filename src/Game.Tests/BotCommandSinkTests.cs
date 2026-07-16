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
			sinkA.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
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
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			logicSink.Update(0f);

			var ctxDirect = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 21);
			var logicDirect = new GameLogic(ctxDirect);
			logicDirect.Update(0f);
			logicDirect.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgA, ActionId = MultiOrgTestSupport.SpendGoldActionId, CountryId = "" });
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
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			logic.Update(0f);

			Assert.Equal(goldBefore - 50.0, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA));
			Assert.Contains(logger.Infos, m => m.Contains("duplicate"));
		}

		[Fact]
		void distinct_plays_in_same_phase_are_all_emitted() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 23);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);

			double goldBefore = OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA);
			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			sink.PlayOrgCard(MultiOrgTestSupport.DiscoverActionId);
			logic.Update(0f);

			Assert.Equal(goldBefore - 50.0, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA));

			var discovered = new HashSet<string>();
			int[] req = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == MultiOrgTestSupport.OrgA) { discovered.Add(dcs[i].CountryId); }
				}
			}
			Assert.True(discovered.Count > 1);
		}

		[Fact]
		void begin_decision_phase_resets_duplicate_guard() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 24);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);

			double goldStart = OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA);

			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			logic.Update(0f);
			Assert.Equal(goldStart - 50.0, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA));

			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			logic.Update(0f);
			Assert.Equal(goldStart - 100.0, OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA));
		}

		[Fact]
		void sink_interface_exposes_only_whitelisted_members() {
			var methods = typeof(IBotCommandSink).GetMethods();
			Assert.Equal(2, methods.Length);
			Assert.Contains(methods, m =>
				m.Name == "PlayOrgCard" && !m.IsGenericMethod &&
				m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
			Assert.Contains(methods, m =>
				m.Name == "PlayCountryCard" && !m.IsGenericMethod &&
				m.GetParameters().Length == 2 && m.GetParameters().All(p => p.ParameterType == typeof(string)));
		}
	}
}
