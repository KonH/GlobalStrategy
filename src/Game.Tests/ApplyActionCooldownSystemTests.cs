using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ApplyActionCooldownSystemTests {
		static readonly DateTime Now = new DateTime(1880, 6, 1);

		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "declare_war", OwnerType = "country" },
					new ActionDefinition { ActionId = "make_friend", OwnerType = "country" },
					new ActionDefinition { ActionId = "org_card", OwnerType = "org" }
				}
			};
		}

		static int AddSucceededCard(World world, string orgId, string actionId, string? countryId) {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			if (countryId != null) {
				world.Add(e, new CountryContext { CountryId = countryId });
			}
			world.Add(e, new CardUse());
			world.Add(e, new ActionSucceeded());
			return e;
		}

		static List<ActionCooldownState> AllCooldowns(World world) {
			var result = new List<ActionCooldownState>();
			int[] required = { TypeId<ActionCooldownState>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ActionCooldownState[] states = arch.GetColumn<ActionCooldownState>();
				for (int i = 0; i < arch.Count; i++) {
					result.Add(states[i]);
				}
			}
			return result;
		}

		[Fact]
		void successful_country_card_play_creates_tracking_entity_with_correct_end_time() {
			var config = BuildActionConfig();
			var settings = new GameSettings { CardCooldownDays = 7 };
			var world = new World();
			AddSucceededCard(world, "OrgA", "declare_war", "Prussia");

			ApplyActionCooldownSystem.Update(world, Now, settings, config);

			var cooldowns = AllCooldowns(world);
			Assert.Single(cooldowns);
			Assert.Equal("OrgA", cooldowns[0].OrgId);
			Assert.Equal("declare_war", cooldowns[0].ActionId);
			Assert.Equal(Now.AddDays(7), cooldowns[0].EndTime);
		}

		[Fact]
		void replaying_the_same_pair_updates_rather_than_duplicates_the_tracking_entity() {
			var config = BuildActionConfig();
			var settings = new GameSettings { CardCooldownDays = 7 };
			var world = new World();
			AddSucceededCard(world, "OrgA", "declare_war", "Prussia");

			ApplyActionCooldownSystem.Update(world, Now, settings, config);
			var firstEntities = new List<int>(GetCooldownEntities(world));

			var laterCard = AddSucceededCard(world, "OrgA", "declare_war", "Austria");
			DateTime later = Now.AddDays(2);
			ApplyActionCooldownSystem.Update(world, later, settings, config);
			var secondEntities = new List<int>(GetCooldownEntities(world));

			Assert.Single(secondEntities);
			Assert.Equal(firstEntities[0], secondEntities[0]);
			var cooldowns = AllCooldowns(world);
			Assert.Single(cooldowns);
			Assert.Equal(later.AddDays(7), cooldowns[0].EndTime);
		}

		[Fact]
		void org_owned_card_play_creates_no_tracking_entity() {
			var config = BuildActionConfig();
			var settings = new GameSettings { CardCooldownDays = 7 };
			var world = new World();
			AddSucceededCard(world, "OrgA", "org_card", null);

			ApplyActionCooldownSystem.Update(world, Now, settings, config);

			Assert.Empty(AllCooldowns(world));
		}

		[Fact]
		void two_different_targets_sharing_the_same_action_id_feed_the_same_tracking_entity() {
			var config = BuildActionConfig();
			var settings = new GameSettings { CardCooldownDays = 7 };
			var world = new World();

			AddSucceededCard(world, "OrgA", "declare_war", "Spain");
			ApplyActionCooldownSystem.Update(world, Now, settings, config);

			AddSucceededCard(world, "OrgA", "declare_war", "Portugal");
			ApplyActionCooldownSystem.Update(world, Now.AddDays(1), settings, config);

			var cooldowns = AllCooldowns(world);
			Assert.Single(cooldowns);
			Assert.Equal("declare_war", cooldowns[0].ActionId);
			Assert.Equal("OrgA", cooldowns[0].OrgId);
			Assert.Equal(Now.AddDays(1).AddDays(7), cooldowns[0].EndTime);
		}

		[Fact]
		void two_different_orgs_playing_the_same_action_id_produce_two_independent_tracking_entities() {
			var config = BuildActionConfig();
			var settings = new GameSettings { CardCooldownDays = 7 };
			var world = new World();

			AddSucceededCard(world, "OrgA", "declare_war", "Prussia");
			AddSucceededCard(world, "OrgB", "declare_war", "Prussia");
			ApplyActionCooldownSystem.Update(world, Now, settings, config);

			var cooldowns = AllCooldowns(world);
			Assert.Equal(2, cooldowns.Count);
			Assert.Contains(cooldowns, c => c.OrgId == "OrgA" && c.ActionId == "declare_war");
			Assert.Contains(cooldowns, c => c.OrgId == "OrgB" && c.ActionId == "declare_war");
		}

		static IEnumerable<int> GetCooldownEntities(World world) {
			int[] required = { TypeId<ActionCooldownState>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				for (int i = 0; i < arch.Count; i++) {
					yield return arch.Entities[i];
				}
			}
		}
	}
}
