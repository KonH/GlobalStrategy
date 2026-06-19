using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class CountryActionSystemTests {
		const string OrgId = "TestOrg";
		const string CountryId = "France";
		const string ActionId = "sphere_of_pressure";
		const string AdvisorActionId = "letter_of_commendation_diplomacy_advisor";
		const string CharId = "char_001";

		static CountryActionConfig BuildConfig(
			string actionId = ActionId,
			float successRateBase = 1.0f,
			int influenceThreshold = 0,
			int cooldownMonths = 1,
			int influenceOnSuccess = 10,
			double goldCost = 50,
			string opinionSourceId = "",
			int opinionValue = 5,
			int opinionChangeValue = 0,
			int successRateInfluenceDivisor = 0) {
			return new CountryActionConfig {
				Actions = new List<CountryActionDefinition> {
					new CountryActionDefinition {
						ActionId = actionId,
						SuccessRateBase = successRateBase,
						InfluenceThreshold = influenceThreshold,
						CooldownMonths = cooldownMonths,
						InfluenceOnSuccess = influenceOnSuccess,
						GoldCost = goldCost,
						OpinionModifierSourceId = opinionSourceId,
						OpinionModifierValue = opinionValue,
						OpinionModifierChangeValue = opinionChangeValue,
						SuccessRateInfluenceDivisor = successRateInfluenceDivisor
					}
				}
			};
		}

		static (World world, int goldEntity, int cardHandEntity) BuildWorld(double gold = 500) {
			var world = new World();

			int goldEntity = world.Create();
			world.Add(goldEntity, new ResourceOwner(OrgId));
			world.Add(goldEntity, new Resource { ResourceId = "gold", Value = gold });

			int cardHandEntity = world.Create();
			world.Add(cardHandEntity, new CountryActionCard {
				OrgId = OrgId, CountryId = CountryId, ActionId = ActionId, TargetCharacterId = ""
			});
			world.Add(cardHandEntity, new InHand { SlotIndex = 0 });

			return (world, goldEntity, cardHandEntity);
		}

		static PlayCountryActionCommand MakeCmd(string actionId = ActionId, string targetCharId = "") {
			return new PlayCountryActionCommand {
				OrgId = OrgId,
				CountryId = CountryId,
				ActionId = actionId,
				TargetCharacterId = targetCharId
			};
		}

		static int CountInfluenceEffects(World world, string orgId, string countryId) {
			int total = 0;
			int[] req = { TypeId<InfluenceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				InfluenceEffect[] effs = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effs[i].OrgId == orgId && effs[i].CountryId == countryId) { total += effs[i].Value; }
				}
			}
			return total;
		}

		static int CountHandCards(World world) {
			int count = 0;
			int[] req = { TypeId<CountryActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) { count += arch.Count; }
			return count;
		}

		static bool HasCooldown(World world, int entity) => world.Has<ActionCooldown>(entity);

		[Fact]
		void play_returns_not_executed_if_no_gold() {
			var (world, goldEntity, _) = BuildWorld(gold: 10);
			var config = BuildConfig(goldCost: 50);
			var result = CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));
			Assert.False(result.Executed);
			Assert.Equal(10.0, world.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void play_deducts_gold_on_execution() {
			var (world, goldEntity, _) = BuildWorld(gold: 200);
			var config = BuildConfig(goldCost: 50);
			var result = CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));
			Assert.True(result.Executed);
			Assert.Equal(150.0, world.Get<Resource>(goldEntity).Value);
		}

		[Fact]
		void play_returns_not_executed_below_influence_threshold() {
			var (world, _, _) = BuildWorld();
			var config = BuildConfig(influenceThreshold: 10);
			var result = CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));
			Assert.False(result.Executed);
		}

		[Fact]
		void play_success_adds_influence() {
			var (world, _, _) = BuildWorld();
			var config = BuildConfig(successRateBase: 1.0f, influenceOnSuccess: 10);
			var result = CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));
			Assert.True(result.Success);
			Assert.Equal(10, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_failure_does_not_add_influence() {
			var (world, _, _) = BuildWorld();
			var config = BuildConfig(successRateBase: 0.0f, influenceOnSuccess: 10);
			var result = CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));
			Assert.False(result.Success);
			Assert.Equal(0, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_success_capped_at_pool_limit() {
			var (world, _, _) = BuildWorld();

			// Fill pool to 95
			int fillEntity = world.Create();
			world.Add(fillEntity, new InfluenceEffect { OrgId = OrgId, CountryId = CountryId, Value = 95 });

			var config = BuildConfig(successRateBase: 1.0f, influenceOnSuccess: 10);
			CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));

			// Should only add 5 to reach cap of 100
			Assert.Equal(100, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_success_adds_no_influence_when_pool_full() {
			var (world, _, _) = BuildWorld();

			int fillEntity = world.Create();
			world.Add(fillEntity, new InfluenceEffect { OrgId = OrgId, CountryId = CountryId, Value = 100 });

			var config = BuildConfig(successRateBase: 1.0f, influenceOnSuccess: 10);
			CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));

			Assert.Equal(100, CountInfluenceEffects(world, OrgId, CountryId));
		}

		[Fact]
		void play_assigns_cooldown_to_all_copies() {
			var (world, _, cardHandEntity) = BuildWorld();

			int cardDeckEntity = world.Create();
			world.Add(cardDeckEntity, new CountryActionCard {
				OrgId = OrgId, CountryId = CountryId, ActionId = ActionId, TargetCharacterId = ""
			});

			var config = BuildConfig(cooldownMonths: 1);
			CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));

			Assert.True(HasCooldown(world, cardHandEntity));
			Assert.True(HasCooldown(world, cardDeckEntity));
		}

		[Fact]
		void tick_removes_expired_cooldown() {
			var (world, _, cardEntity) = BuildWorld();

			var pastTime = DateTime.UtcNow.AddMonths(-1);
			world.Add(cardEntity, new ActionCooldown { CooldownEndTime = pastTime });

			CountryActionSystem.TickCooldowns(world, DateTime.UtcNow);

			Assert.False(HasCooldown(world, cardEntity));
		}

		[Fact]
		void tick_keeps_active_cooldown() {
			var (world, _, cardEntity) = BuildWorld();

			var futureTime = DateTime.UtcNow.AddMonths(2);
			world.Add(cardEntity, new ActionCooldown { CooldownEndTime = futureTime });

			CountryActionSystem.TickCooldowns(world, DateTime.UtcNow);

			Assert.True(HasCooldown(world, cardEntity));
		}

		[Fact]
		void play_draws_from_eligible_deck_card() {
			var (world, _, _) = BuildWorld();

			// Deck card uses a different action so it isn't put on cooldown when the hand card is played
			int deckCard = world.Create();
			world.Add(deckCard, new CountryActionCard {
				OrgId = OrgId, CountryId = CountryId, ActionId = AdvisorActionId, TargetCharacterId = ""
			});

			var config = new CountryActionConfig {
				Actions = new List<CountryActionDefinition> {
					new CountryActionDefinition { ActionId = ActionId, SuccessRateBase = 1.0f, CooldownMonths = 1, GoldCost = 50 },
					new CountryActionDefinition { ActionId = AdvisorActionId, SuccessRateBase = 1.0f, CooldownMonths = 1, GoldCost = 50, InfluenceThreshold = 0 }
				}
			};
			CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));

			Assert.Equal(1, CountHandCards(world));
		}

		[Fact]
		void play_success_adds_opinion_modifier_to_target_character() {
			var (world, _, _) = BuildWorld();

			int charEntity = world.Create();
			world.Add(charEntity, new Character { CharacterId = CharId });
			world.Add(charEntity, new CharacterOpinion {
				ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>>()
			});

			var config = BuildConfig(successRateBase: 1.0f, opinionSourceId: "commendation", opinionValue: 5, opinionChangeValue: 0);
			CountryActionSystem.ProcessPlayCountryAction(
				world, MakeCmd(targetCharId: CharId), config, DateTime.UtcNow, new Random(1));

			ref CharacterOpinion opinion = ref world.Get<CharacterOpinion>(charEntity);
			Assert.True(opinion.ModifiersPerOrg.ContainsKey(OrgId));
			Assert.True(opinion.ModifiersPerOrg[OrgId].Count > 0);
			Assert.Equal("commendation", opinion.ModifiersPerOrg[OrgId][0].SourceId);
		}

		[Fact]
		void play_failure_does_not_add_opinion_modifier() {
			var (world, _, _) = BuildWorld();

			int charEntity = world.Create();
			world.Add(charEntity, new Character { CharacterId = CharId });
			world.Add(charEntity, new CharacterOpinion {
				ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>>()
			});

			var config = BuildConfig(successRateBase: 0.0f, opinionSourceId: "commendation");
			CountryActionSystem.ProcessPlayCountryAction(
				world, MakeCmd(targetCharId: CharId), config, DateTime.UtcNow, new Random(1));

			ref CharacterOpinion opinion = ref world.Get<CharacterOpinion>(charEntity);
			bool hasModifier = opinion.ModifiersPerOrg != null && opinion.ModifiersPerOrg.ContainsKey(OrgId);
			Assert.False(hasModifier);
		}

		[Fact]
		void cooldown_cards_not_drawn_to_hand() {
			var (world, _, _) = BuildWorld();

			int deckCard = world.Create();
			world.Add(deckCard, new CountryActionCard {
				OrgId = OrgId, CountryId = CountryId, ActionId = ActionId, TargetCharacterId = ""
			});
			world.Add(deckCard, new ActionCooldown { CooldownEndTime = DateTime.UtcNow.AddMonths(1) });

			var config = BuildConfig();
			CountryActionSystem.ProcessPlayCountryAction(world, MakeCmd(), config, DateTime.UtcNow, new Random(1));

			Assert.Equal(0, CountHandCards(world));
		}
	}
}
