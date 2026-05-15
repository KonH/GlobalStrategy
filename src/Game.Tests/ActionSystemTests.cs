using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ActionSystemTests {
		static (World world, int goldEntity, int cardEntity, int pmEntity) BuildActionWorld(float successRate = 1.0f) {
			var world = new World();

			// Countries
			int russiaEntity = world.Create();
			world.Add(russiaEntity, new Country("Russian_Empire"));
			world.Add(russiaEntity, new Player());
			world.Add(russiaEntity, new IsDiscovered());

			int franceEntity = world.Create();
			world.Add(franceEntity, new Country("France"));

			int ottomanEntity = world.Create();
			world.Add(ottomanEntity, new Country("Ottoman_Empire"));

			// Organization
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Illuminati", DisplayName = "Illuminati" });

			// Gold resource
			int goldEntity = world.Create();
			world.Add(goldEntity, new ResourceOwner("Illuminati"));
			world.Add(goldEntity, new Resource { ResourceId = "gold", Value = 500 });

			// ActionOwner
			int ownerEntity = world.Create();
			world.Add(ownerEntity, new ActionOwner { OwnerId = "Illuminati", OwnerType = "org", HandSize = 1 });

			// ActionCard in hand
			int cardEntity = world.Create();
			world.Add(cardEntity, new ActionCard { ActionId = "discover_country", OwnerId = "Illuminati" });
			world.Add(cardEntity, new InHand { SlotIndex = 0 });

			// ProximityMapData (empty = equal weights)
			int pmEntity = world.Create();
			world.Add(pmEntity, new ProximityMapData { Distances = new Dictionary<(string, string), float>() });

			return (world, goldEntity, cardEntity, pmEntity);
		}

		static ActionConfig BuildActionConfig(float successRate = 1.0f) {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 1 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "discover_country",
						SuccessRate = successRate,
						Prices = new List<ActionPrice> {
							new ActionPrice { ResourceId = "gold", Amount = 100 }
						},
						MinCountryChance = 0.01f
					}
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = "Illuminati", ActionIds = new List<string> { "discover_country" } }
				}
			};
		}

		static PlayActionCommand MakeCmd() {
			return new PlayActionCommand { OwnerId = "Illuminati", ActionId = "discover_country" };
		}

		static int GetProximityEntity(World world) {
			int[] req = { TypeId<ProximityMapData>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) { return arch.Entities[0]; }
			}
			return -1;
		}

		[Fact]
		void play_action_deducts_gold() {
			var (world, goldEntity, _, pmEntity) = BuildActionWorld();
			var config = BuildActionConfig(1.0f);
			var rng = new Random(42);

			ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			Assert.Equal(400.0, world.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void play_action_insufficient_gold_returns_not_executed() {
			var (world, goldEntity, _, pmEntity) = BuildActionWorld();
			// Set gold to 50
			ref Resource gold = ref world.Get<Resource>(goldEntity);
			gold.Value = 50;

			var config = BuildActionConfig(1.0f);
			var rng = new Random(42);

			var result = ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			Assert.False(result.Executed);
			Assert.Equal(50.0, world.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void play_action_success_marks_country_discovered() {
			var (world, _, _, pmEntity) = BuildActionWorld();
			var config = BuildActionConfig(1.0f);
			var rng = new Random(42);

			ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			int discoveredCount = 0;
			int[] req = { TypeId<Country>.Value, TypeId<IsDiscovered>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				discoveredCount += arch.Count;
			}
			// Russia was already discovered + one more
			Assert.True(discoveredCount >= 2);
		}

		[Fact]
		void play_action_failure_does_not_discover() {
			var (world, _, _, pmEntity) = BuildActionWorld();
			var config = BuildActionConfig(0.0f);
			var rng = new Random(42);

			ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			// Only Russia should be discovered
			int discoveredCount = 0;
			int[] req = { TypeId<Country>.Value, TypeId<IsDiscovered>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				discoveredCount += arch.Count;
			}
			Assert.Equal(1, discoveredCount);
		}

		[Fact]
		void play_action_removes_card_from_hand() {
			// With a single card in pool: after playing, card goes to deck, then immediately re-drawn.
			// Verify that exactly 1 card is in hand (the cycle completes) and all cards still belong to owner.
			var (world, _, cardEntity, pmEntity) = BuildActionWorld();
			var config = BuildActionConfig(1.0f);
			var rng = new Random(42);

			// Before play: 1 in hand
			int handBefore = 0;
			int[] reqBefore = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(reqBefore, null)) {
				handBefore += arch.Count;
			}
			Assert.Equal(1, handBefore);

			ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			// After play: still 1 in hand (card cycled through deck and re-drawn)
			int handAfter = 0;
			int[] reqAfter = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(reqAfter, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId == "Illuminati") { handAfter++; }
				}
			}
			Assert.Equal(1, handAfter);
		}

		[Fact]
		void play_action_draws_new_card_from_deck() {
			var (world, _, _, pmEntity) = BuildActionWorld();

			// Add a second card to deck (no InHand)
			int card2Entity = world.Create();
			world.Add(card2Entity, new ActionCard { ActionId = "discover_country", OwnerId = "Illuminati" });

			var config = BuildActionConfig(1.0f);
			var rng = new Random(42);

			ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			// Exactly one card should be in hand
			int handCount = 0;
			int[] req = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId == "Illuminati") { handCount++; }
				}
			}
			Assert.Equal(1, handCount);
		}

		[Fact]
		void play_action_executed_sets_result() {
			var (world, _, _, pmEntity) = BuildActionWorld();
			var config = BuildActionConfig(1.0f);
			var rng = new Random(42);

			var result = ActionSystem.ProcessPlayAction(world, MakeCmd(), config, pmEntity, rng);

			Assert.True(result.Executed);
			Assert.True(result.Success);
		}
	}
}
