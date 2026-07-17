using System.Collections.Generic;
using GS.Game.Commands;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DeterminismTests {
		static readonly List<string> Participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };

		static GameLogic BuildSeededLogic(int seed) {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: seed);
			return new GameLogic(ctx);
		}

		static void RunScriptedTicks(GameLogic logic, int tickCount) {
			logic.Update(0f);
			for (int tick = 0; tick < tickCount; tick++) {
				if (tick == 5) {
					logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgA, ActionId = MultiOrgTestSupport.SpendGoldActionId });
				}
				if (tick == 10) {
					logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgB, ActionId = MultiOrgTestSupport.DiscoverActionId });
				}
				logic.Update(24f);
			}
		}

		static List<(string ActionId, int SlotIndex)> GetHandContents(GameLogic logic, string orgId) {
			var result = new List<(string, int)>();
			int[] req = { ECS.TypeId<GS.Game.Components.GameAction>.Value, ECS.TypeId<GS.Game.Components.OrgContext>.Value, ECS.TypeId<GS.Game.Components.CardInHand>.Value };
			int[] exclude = { ECS.TypeId<GS.Game.Components.CountryContext>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(req, exclude)) {
				GS.Game.Components.GameAction[] actions = arch.GetColumn<GS.Game.Components.GameAction>();
				GS.Game.Components.OrgContext[] orgs = arch.GetColumn<GS.Game.Components.OrgContext>();
				GS.Game.Components.CardInHand[] hands = arch.GetColumn<GS.Game.Components.CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId) { result.Add((actions[i].ActionId, hands[i].SlotIndex)); }
				}
			}
			result.Sort((a, b) => a.Item1 == b.Item1 ? a.Item2.CompareTo(b.Item2) : string.CompareOrdinal(a.Item1, b.Item1));
			return result;
		}

		[Fact]
		void same_seed_and_commands_produce_identical_end_state() {
			var logicA = BuildSeededLogic(777);
			var logicB = BuildSeededLogic(777);

			RunScriptedTicks(logicA, 400);
			RunScriptedTicks(logicB, 400);

			foreach (string orgId in Participants) {
				Assert.Equal(OrgMetrics.GetTotalControl(logicA.World, orgId), OrgMetrics.GetTotalControl(logicB.World, orgId));
				Assert.Equal(OrgMetrics.GetGold(logicA.World, orgId), OrgMetrics.GetGold(logicB.World, orgId));
				Assert.Equal(OrgMetrics.GetControlByCountry(logicA.World, orgId), OrgMetrics.GetControlByCountry(logicB.World, orgId));
				Assert.Equal(GetHandContents(logicA, orgId), GetHandContents(logicB, orgId));
			}

			Assert.Equal(logicA.VisualState.Time.CurrentTime, logicB.VisualState.Time.CurrentTime);
		}

		[Fact]
		void same_seed_and_commands_produce_identical_monthly_timeline() {
			var logicA = BuildSeededLogic(2024);
			var logicB = BuildSeededLogic(2024);

			var timelineA = new List<(int Month, int Year, Dictionary<string, int> Control, Dictionary<string, double> Gold)>();
			var timelineB = new List<(int Month, int Year, Dictionary<string, int> Control, Dictionary<string, double> Gold)>();

			RunAndSampleMonthly(logicA, 400, timelineA);
			RunAndSampleMonthly(logicB, 400, timelineB);

			Assert.Equal(timelineA.Count, timelineB.Count);
			for (int i = 0; i < timelineA.Count; i++) {
				Assert.Equal(timelineA[i].Month, timelineB[i].Month);
				Assert.Equal(timelineA[i].Year, timelineB[i].Year);
				foreach (string orgId in Participants) {
					Assert.Equal(timelineA[i].Control[orgId], timelineB[i].Control[orgId]);
					Assert.Equal(timelineA[i].Gold[orgId], timelineB[i].Gold[orgId]);
				}
			}
		}

		static void RunAndSampleMonthly(
			GameLogic logic, int tickCount,
			List<(int Month, int Year, Dictionary<string, int> Control, Dictionary<string, double> Gold)> timeline) {
			logic.Update(0f);
			int lastMonth = -1, lastYear = -1;
			for (int tick = 0; tick < tickCount; tick++) {
				if (tick == 5) {
					logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgA, ActionId = MultiOrgTestSupport.SpendGoldActionId });
				}
				if (tick == 10) {
					logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgB, ActionId = MultiOrgTestSupport.DiscoverActionId });
				}
				logic.Update(24f);

				var date = logic.VisualState.Time.CurrentTime;
				if (date.Month != lastMonth || date.Year != lastYear) {
					lastMonth = date.Month;
					lastYear = date.Year;
					var control = new Dictionary<string, int>();
					var gold = new Dictionary<string, double>();
					foreach (string orgId in Participants) {
						control[orgId] = OrgMetrics.GetTotalControl(logic.World, orgId);
						gold[orgId] = OrgMetrics.GetGold(logic.World, orgId);
					}
					timeline.Add((lastMonth, lastYear, control, gold));
				}
			}
		}
	}
}
