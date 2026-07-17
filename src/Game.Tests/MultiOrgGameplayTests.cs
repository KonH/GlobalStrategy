using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class MultiOrgGameplayTests {
		static double GetGold(World world, string orgId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == orgId && resources[i].ResourceId == "gold") { return resources[i].Value; }
				}
			}
			return -1;
		}

		static int CountHandCards(World world, string orgId) {
			int count = 0;
			int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
			int[] exclude = { TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, exclude)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId) { count++; }
				}
			}
			return count;
		}

		static bool CardUsedThisTurn(World world, string orgId, string actionId) {
			int[] req = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CardUse>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId && actions[i].ActionId == actionId) { return true; }
				}
			}
			return false;
		}

		static int GetTotalControl(World world, string orgId) {
			int total = 0;
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].OrgId == orgId) { total += effects[i].Value; }
				}
			}
			return total;
		}

		[Fact]
		void two_orgs_play_cards_independently_without_cross_interference() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			double goldABefore = GetGold(world, MultiOrgTestSupport.OrgA);
			double goldBBefore = GetGold(world, MultiOrgTestSupport.OrgB);
			int handABefore = CountHandCards(world, MultiOrgTestSupport.OrgA);
			int handBBefore = CountHandCards(world, MultiOrgTestSupport.OrgB);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgA, ActionId = MultiOrgTestSupport.SpendGoldActionId });
			logic.Commands.Push(new PlayCardActionCommand { OrgId = MultiOrgTestSupport.OrgB, ActionId = MultiOrgTestSupport.SpendGoldActionId });
			logic.Update(0f);

			Assert.Equal(goldABefore - 50.0, GetGold(world, MultiOrgTestSupport.OrgA));
			Assert.Equal(goldBBefore - 50.0, GetGold(world, MultiOrgTestSupport.OrgB));

			Assert.True(CardUsedThisTurn(world, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.SpendGoldActionId));
			Assert.True(CardUsedThisTurn(world, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.SpendGoldActionId));

			// Hand sizes are refilled back up to their configured size after redraw.
			Assert.Equal(handABefore, CountHandCards(world, MultiOrgTestSupport.OrgA));
			Assert.Equal(handBBefore, CountHandCards(world, MultiOrgTestSupport.OrgB));
		}

		[Fact]
		void change_control_and_debug_change_gold_affect_only_named_org() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			int controlABefore = GetTotalControl(world, MultiOrgTestSupport.OrgA);
			int controlBBefore = GetTotalControl(world, MultiOrgTestSupport.OrgB);
			double goldBBefore = GetGold(world, MultiOrgTestSupport.OrgB);

			logic.Commands.Push(new ChangeControlCommand { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.HqA, Delta = 15 });
			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = 250.0 });
			logic.Update(0f);

			Assert.Equal(controlABefore + 15, GetTotalControl(world, MultiOrgTestSupport.OrgA));
			Assert.Equal(controlBBefore, GetTotalControl(world, MultiOrgTestSupport.OrgB));
			Assert.Equal(goldBBefore, GetGold(world, MultiOrgTestSupport.OrgB));
		}
	}
}
