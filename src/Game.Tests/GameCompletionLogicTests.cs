using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class GameCompletionLogicTests {
		static readonly List<string> Participants = new List<string> {
			MultiOrgTestSupport.OrgA,
			MultiOrgTestSupport.OrgB
		};

		static GameLogic BuildLogic() {
			var logic = new GameLogic(MultiOrgTestSupport.BuildContext(Participants, rngSeed: 41));
			logic.Update(0f);
			return logic;
		}

		static void GiveTotalControl(GameLogic logic, string organizationId) {
			foreach (string countryId in new[] {
				MultiOrgTestSupport.HqA,
				MultiOrgTestSupport.HqB,
				MultiOrgTestSupport.ExtraCountry1,
				MultiOrgTestSupport.ExtraCountry2
			}) {
				logic.Commands.Push(new ChangeControlCommand {
					OrgId = organizationId,
					CountryId = countryId,
					Delta = logic.MaxControlPool
				});
			}
		}

		static int CountActions(World world) {
			int count = 0;
			int[] required = { TypeId<GameAction>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				count += archetype.Count;
			}
			return count;
		}

		static double GetGold(World world) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				for (int i = 0; i < archetype.Count; i++) {
					if (owners[i].OwnerId == MultiOrgTestSupport.OrgA && resources[i].ResourceId == "gold") {
						return resources[i].Value;
					}
				}
			}
			return -1;
		}

		static int GetControl(World world, string countryId) {
			int total = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] effects = archetype.GetColumn<ControlEffect>();
				for (int i = 0; i < archetype.Count; i++) {
					if (effects[i].OrgId == MultiOrgTestSupport.OrgA && effects[i].CountryId == countryId) {
						total += effects[i].Value;
					}
				}
			}
			return total;
		}

		static (OrganizationGameResult A, OrganizationGameResult B) ReadOutcomes(World world) {
			OrganizationGameResult a = OrganizationGameResult.InProgress;
			OrganizationGameResult b = OrganizationGameResult.InProgress;
			int[] required = { TypeId<Organization>.Value, TypeId<OrganizationGameOutcome>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				OrganizationGameOutcome[] outcomes = archetype.GetColumn<OrganizationGameOutcome>();
				for (int i = 0; i < archetype.Count; i++) {
					if (organizations[i].OrganizationId == MultiOrgTestSupport.OrgA) { a = outcomes[i].Result; }
					if (organizations[i].OrganizationId == MultiOrgTestSupport.OrgB) { b = outcomes[i].Result; }
				}
			}
			return (a, b);
		}

		[Fact]
		void projection_reports_in_progress_then_player_win_or_lose() {
			GameLogic winning = BuildLogic();
			Assert.Equal(GameResult.InProgress, winning.VisualState.GameCompletion.Result);
			GiveTotalControl(winning, MultiOrgTestSupport.OrgA);
			winning.Update(0f);
			Assert.Equal(GameResult.Win, winning.VisualState.GameCompletion.Result);

			GameLogic losing = BuildLogic();
			GiveTotalControl(losing, MultiOrgTestSupport.OrgB);
			losing.Update(0f);
			Assert.Equal(GameResult.Lose, losing.VisualState.GameCompletion.Result);
			Assert.Equal(MultiOrgTestSupport.OrgB, losing.VisualState.GameCompletion.WinnerOrganizationId);
		}

		[Fact]
		void winning_tick_applies_all_mutations_and_publishes_the_complete_final_state() {
			GameLogic logic = BuildLogic();
			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = 77.0 });
			GiveTotalControl(logic, MultiOrgTestSupport.OrgA);

			logic.Update(24f);

			Assert.True(logic.IsCompleted);
			Assert.Equal(GameResult.Win, logic.VisualState.GameCompletion.Result);
			Assert.Equal(1077.0, GetGold(logic.World));
			Assert.Equal(logic.MaxControlPool - 10, GetControl(logic.World, MultiOrgTestSupport.HqB));
			var outcomes = ReadOutcomes(logic.World);
			Assert.Equal(OrganizationGameResult.Winner, outcomes.A);
			Assert.Equal(OrganizationGameResult.Loser, outcomes.B);
		}

		[Fact]
		void terminal_updates_discard_gameplay_commands_and_freeze_published_and_ecs_state() {
			GameLogic logic = BuildLogic();
			GiveTotalControl(logic, MultiOrgTestSupport.OrgA);
			logic.Update(0f);
			DateTime time = logic.VisualState.Time.CurrentTime;
			double gold = GetGold(logic.World);
			int control = GetControl(logic.World, MultiOrgTestSupport.HqB);
			int actionCount = CountActions(logic.World);
			var outcomes = ReadOutcomes(logic.World);

			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = 500.0 });
			logic.Commands.Push(new ChangeControlCommand { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.HqB, Delta = -50 });
			logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgA, ActionId = MultiOrgTestSupport.SpendGoldActionId });
			logic.RecordBotAction(MultiOrgTestSupport.OrgB, "feature", "action", MultiOrgTestSupport.HqA);

			logic.Update(2400f);

			Assert.Equal(time, logic.VisualState.Time.CurrentTime);
			Assert.Equal(gold, GetGold(logic.World));
			Assert.Equal(control, GetControl(logic.World, MultiOrgTestSupport.HqB));
			Assert.Equal(actionCount, CountActions(logic.World));
			Assert.Equal(outcomes, ReadOutcomes(logic.World));
			Assert.Equal(GameResult.Win, logic.VisualState.GameCompletion.Result);
		}
	}
}
