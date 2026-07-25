using ECS;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class InitActionFromPlayCardSystemTests {
		const string OrgId = "OrgA";
		const string CountryId = "Home";
		const string ActionId = "stop_friendship";

		static int AddCountryCardInHand(World world, string orgId, string countryId, string actionId, int slotIndex, string targetCountryId = "") {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			world.Add(e, new CountryContext { CountryId = countryId });
			world.Add(e, new CardInHand { SlotIndex = slotIndex });
			if (!string.IsNullOrEmpty(targetCountryId)) {
				world.Add(e, new RelationCardTarget { TargetCountryId = targetCountryId, Kind = RelationKind.Friend });
			}
			return e;
		}

		static void RunCommand(World world, PlayCardActionCommand cmd) {
			InitActionFromPlayCardSystem.Update(world, new ReadCommands<PlayCardActionCommand>(new[] { cmd }));
		}

		[Fact]
		void picks_the_hand_entity_whose_relation_card_target_matches_the_command() {
			var world = new World();
			int franceCard = AddCountryCardInHand(world, OrgId, CountryId, ActionId, 0, "France");
			int germanyCard = AddCountryCardInHand(world, OrgId, CountryId, ActionId, 1, "Germany");

			RunCommand(world, new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = ActionId, TargetCountryId = "France" });

			Assert.True(world.Has<CardUse>(franceCard));
			Assert.False(world.Has<CardUse>(germanyCard));
		}

		[Fact]
		void picks_the_other_instance_when_the_command_names_the_other_country() {
			var world = new World();
			int franceCard = AddCountryCardInHand(world, OrgId, CountryId, ActionId, 0, "France");
			int germanyCard = AddCountryCardInHand(world, OrgId, CountryId, ActionId, 1, "Germany");

			RunCommand(world, new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = ActionId, TargetCountryId = "Germany" });

			Assert.False(world.Has<CardUse>(franceCard));
			Assert.True(world.Has<CardUse>(germanyCard));
		}

		[Fact]
		void does_not_match_a_relation_targeted_entity_when_command_has_no_target_country() {
			var world = new World();
			int franceCard = AddCountryCardInHand(world, OrgId, CountryId, ActionId, 0, "France");

			RunCommand(world, new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = ActionId });

			Assert.False(world.Has<CardUse>(franceCard));
		}

		[Fact]
		void plain_cards_without_relation_card_target_are_unaffected() {
			var world = new World();
			int plainCard = AddCountryCardInHand(world, OrgId, CountryId, "raise_control", 0);

			RunCommand(world, new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = "raise_control" });

			Assert.True(world.Has<CardUse>(plainCard));
		}
	}
}
