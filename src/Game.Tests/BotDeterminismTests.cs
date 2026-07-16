using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Bots;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotDeterminismTests {
		static readonly List<string> Participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };

		static (GameLogic Logic, Bot Bot) BuildWithBot(int seed) {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: seed, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);
			var rng = BotRng.Create(seed, MultiOrgTestSupport.OrgA);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, rng, sink);
			return (logic, bot);
		}

		static void RunTickContract(GameLogic logic, Bot bot, int tickCount) {
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			logic.Update(0f);
			for (int tick = 0; tick < tickCount; tick++) {
				bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
				logic.Update(24f);
			}
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
		void same_seed_and_profiles_produce_identical_end_state_and_timeline() {
			var (logicA, botA) = BuildWithBot(555);
			var (logicB, botB) = BuildWithBot(555);

			RunTickContract(logicA, botA, 400);
			RunTickContract(logicB, botB, 400);

			foreach (string orgId in Participants) {
				Assert.Equal(OrgMetrics.GetTotalControl(logicA.World, orgId), OrgMetrics.GetTotalControl(logicB.World, orgId));
				Assert.Equal(OrgMetrics.GetGold(logicA.World, orgId), OrgMetrics.GetGold(logicB.World, orgId));
				Assert.Equal(OrgMetrics.GetControlByCountry(logicA.World, orgId), OrgMetrics.GetControlByCountry(logicB.World, orgId));
				Assert.Equal(GetHandContents(logicA, orgId), GetHandContents(logicB, orgId));
			}
			Assert.Equal(logicA.VisualState.Time.CurrentTime, logicB.VisualState.Time.CurrentTime);
		}

		[Fact]
		void bot_seed_derivation_is_stable() {
			Assert.Equal(1773247666, BotRng.DeriveSeed(12345, "Illuminati"));
			Assert.Equal(1289280367, BotRng.DeriveSeed(12345, "Masons"));
			Assert.Equal(1773252482, BotRng.DeriveSeed(777, "Illuminati"));
		}

		[Fact]
		void different_orgs_get_different_derived_seeds() {
			int seedA = BotRng.DeriveSeed(12345, "Illuminati");
			int seedB = BotRng.DeriveSeed(12345, "Masons");
			Assert.NotEqual(seedA, seedB);
		}
	}
}
