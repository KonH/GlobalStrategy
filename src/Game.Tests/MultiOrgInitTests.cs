using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class MultiOrgInitTests {
		static int FindOrgEntity(World world, string orgId) {
			int[] req = { TypeId<Organization>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrganizationId == orgId) { return arch.Entities[i]; }
				}
			}
			return -1;
		}

		static double GetGold(World world, string orgId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == orgId && resources[i].ResourceId == "gold") { return resources[i].Value; }
				}
			}
			throw new InvalidOperationException($"No gold resource found for {orgId}");
		}

		static bool HasBaseControl(World world, string orgId, string countryId) {
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].OrgId == orgId && effects[i].CountryId == countryId
						&& effects[i].EffectId == $"base_{orgId}") {
						return true;
					}
				}
			}
			return false;
		}

		static List<CharacterSlot> GetSlots(World world, string orgId) {
			var result = new List<CharacterSlot>();
			int[] req = { TypeId<CharacterSlot>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				CharacterSlot[] slots = arch.GetColumn<CharacterSlot>();
				for (int i = 0; i < arch.Count; i++) {
					if (slots[i].OwnerId == orgId) { result.Add(slots[i]); }
				}
			}
			return result;
		}

		static bool HasOrgDeckAndHand(World world, string orgId) {
			int[] req = { TypeId<CardDeck>.Value, TypeId<CardHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId && decks[i].CountryId == "" && hands[i].HandSize > 0) { return true; }
				}
			}
			return false;
		}

		static int CountCardsInHand(World world, string orgId) {
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

		static bool HasCountryDeckForOrg(World world, string orgId, string countryId) {
			int[] req = { TypeId<CardDeck>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId && decks[i].CountryId == countryId) { return true; }
				}
			}
			return false;
		}

		[Fact]
		void each_participating_org_gets_org_entity_gold_base_control_slots_deck_and_hand() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			Assert.NotEqual(-1, FindOrgEntity(world, MultiOrgTestSupport.OrgA));
			Assert.NotEqual(-1, FindOrgEntity(world, MultiOrgTestSupport.OrgB));

			Assert.Equal(1000.0, GetGold(world, MultiOrgTestSupport.OrgA));
			Assert.Equal(500.0, GetGold(world, MultiOrgTestSupport.OrgB));

			Assert.True(HasBaseControl(world, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA));
			Assert.True(HasBaseControl(world, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.HqB));

			foreach (string orgId in participants) {
				var slots = GetSlots(world, orgId);
				Assert.Contains(slots, s => s.RoleId == "master");
				Assert.Single(slots, s => s.RoleId == "master");
				Assert.Single(slots, s => s.RoleId == "agent");

				Assert.True(HasOrgDeckAndHand(world, orgId));
				Assert.True(CountCardsInHand(world, orgId) > 0);

				foreach (var countryId in new[] {
					MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB,
					MultiOrgTestSupport.ExtraCountry1, MultiOrgTestSupport.ExtraCountry2
				}) {
					// No country actions are configured in the test action config, so no
					// country decks are created; only assert org-scoped state above.
					Assert.False(HasCountryDeckForOrg(world, orgId, countryId));
				}
			}
		}

		[Fact]
		void unknown_participating_org_id_fails_fast() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, "DoesNotExist" };
			var errors = new List<string>();
			var logger = new CapturingLogger(errors);
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, logger: logger);
			var logic = new GameLogic(ctx);

			Assert.Throws<InvalidOperationException>(() => logic.Update(0f));
			Assert.NotEmpty(errors);
		}

		[Fact]
		void default_context_initializes_single_org_exactly_as_today() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: null);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			Assert.NotEqual(-1, FindOrgEntity(world, MultiOrgTestSupport.OrgA));
			Assert.Equal(-1, FindOrgEntity(world, MultiOrgTestSupport.OrgB));
			Assert.Equal(1000.0, GetGold(world, MultiOrgTestSupport.OrgA));

			var discovered = new HashSet<string>();
			int[] req = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == MultiOrgTestSupport.OrgA) { discovered.Add(dcs[i].CountryId); }
				}
			}
			Assert.Equal(new HashSet<string> { MultiOrgTestSupport.HqA }, discovered);
		}

		[Fact]
		void non_view_org_slots_are_initialized_with_is_available() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			var slots = GetSlots(world, MultiOrgTestSupport.OrgB);
			foreach (var slot in slots) {
				Assert.True(slot.IsAvailable);
				Assert.Equal("", slot.CharacterId);
			}
		}

		sealed class CapturingLogger : IGameLogger {
			readonly List<string> _errors;
			public CapturingLogger(List<string> errors) => _errors = errors;
			public void LogError(string message) => _errors.Add(message);
			public void LogInfo(string message) { }
			public void LogDebug(string message) { }
		}
	}
}
