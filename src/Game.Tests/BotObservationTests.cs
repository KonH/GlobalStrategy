using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Configs;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotObservationTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static void PutCountryCardInHand(GameLogic logic, string orgId, string actionId) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value
			};
			foreach (var archetype in logic.World.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = archetype.GetColumn<GameAction>();
				OrgContext[] organizations = archetype.GetColumn<OrgContext>();
				CardOwnerType[] owners = archetype.GetColumn<CardOwnerType>();
				for (int i = 0; i < archetype.Count; i++) {
					if (actions[i].ActionId == actionId
						&& organizations[i].OrgId == orgId
						&& owners[i].Value == CardOwnerKind.Country) {
						int entity = archetype.Entities[i];
						if (!logic.World.Has<CardInHand>(entity)) {
							logic.World.Add(entity, new CardInHand { SlotIndex = 0 });
						}
						return;
					}
				}
			}
			throw new InvalidOperationException($"Country card not found: org={orgId} action={actionId}");
		}
		static void AssertCardEqual(BotCardView a, BotCardView b) {
			Assert.Equal(a.ActionId, b.ActionId);
			Assert.Equal(a.SlotIndex, b.SlotIndex);
			Assert.Equal(a.CountryId, b.CountryId);
			Assert.Equal(a.GoldCost, b.GoldCost);
			Assert.Equal(a.IsPlayable, b.IsPlayable);
			Assert.Equal(a.RaisesControl, b.RaisesControl);
			Assert.Equal(a.Cost.Count, b.Cost.Count);
			for (int i = 0; i < a.Cost.Count; i++) {
				Assert.Equal(a.Cost[i].ResourceId, b.Cost[i].ResourceId);
				Assert.Equal(a.Cost[i].Amount, b.Cost[i].Amount);
			}
		}

		static void AssertCountryEqual(BotCountryView a, BotCountryView b) {
			Assert.Equal(a.CountryId, b.CountryId);
			Assert.Equal(a.MyControl, b.MyControl);
			Assert.Equal(a.TotalControl, b.TotalControl);
			Assert.Equal(a.ControlByOrg.Count, b.ControlByOrg.Count);
			for (int i = 0; i < a.ControlByOrg.Count; i++) {
				Assert.Equal(a.ControlByOrg[i].OrgId, b.ControlByOrg[i].OrgId);
				Assert.Equal(a.ControlByOrg[i].Control, b.ControlByOrg[i].Control);
			}
			Assert.Equal(a.Hand.Count, b.Hand.Count);
			for (int i = 0; i < a.Hand.Count; i++) { AssertCardEqual(a.Hand[i], b.Hand[i]); }
			Assert.Equal(a.Characters.Count, b.Characters.Count);
			for (int i = 0; i < a.Characters.Count; i++) {
				Assert.Equal(a.Characters[i].CharacterId, b.Characters[i].CharacterId);
				Assert.Equal(a.Characters[i].RoleId, b.Characters[i].RoleId);
				Assert.Equal(a.Characters[i].OpinionOfMyOrg, b.Characters[i].OpinionOfMyOrg);
			}
			Assert.Equal(a.IsDestroyed, b.IsDestroyed);
			Assert.Equal(a.IsAtWar, b.IsAtWar);
			Assert.Equal(a.WarOpponentCountryId, b.WarOpponentCountryId);
			Assert.Equal(a.OwnWarProgress, b.OwnWarProgress);
			Assert.Equal(a.RivalCountryIds.Count, b.RivalCountryIds.Count);
			for (int i = 0; i < a.RivalCountryIds.Count; i++) {
				Assert.Equal(a.RivalCountryIds[i], b.RivalCountryIds[i]);
			}
			Assert.Equal(a.CountryScore, b.CountryScore);
			Assert.Equal(a.OwnedProvinceCount, b.OwnedProvinceCount);
			Assert.Equal(a.OccupiedOwnedProvinceCount, b.OccupiedOwnedProvinceCount);
			Assert.Equal(a.Recruits, b.Recruits);
			Assert.Equal(a.Damage, b.Damage);
			Assert.Equal(a.Durability, b.Durability);
			Assert.Equal(a.TroopsDamageBonusPercent, b.TroopsDamageBonusPercent);
		}

		static void AssertDrawChoiceEqual(BotCardDrawChoiceView a, BotCardDrawChoiceView b) {
			Assert.Equal(a.ChoiceIndex, b.ChoiceIndex);
			Assert.Equal(a.ActionId, b.ActionId);
			Assert.Equal(a.TargetCountryId, b.TargetCountryId);
			Assert.Equal(a.GoldCost, b.GoldCost);
			Assert.Equal(a.RaisesControl, b.RaisesControl);
			Assert.Equal(a.IsPlayable, b.IsPlayable);
			Assert.Equal(a.IsControlUsable, b.IsControlUsable);
			Assert.Equal(a.Cost.Count, b.Cost.Count);
			for (int i = 0; i < a.Cost.Count; i++) {
				Assert.Equal(a.Cost[i].ResourceId, b.Cost[i].ResourceId);
				Assert.Equal(a.Cost[i].Amount, b.Cost[i].Amount);
			}
		}

		static void AssertObservationsEqual(BotObservation a, BotObservation b) {
			Assert.Equal(a.OrgId, b.OrgId);
			Assert.Equal(a.CurrentDate, b.CurrentDate);
			Assert.Equal(a.Gold, b.Gold);
			Assert.Equal(a.OrgScore, b.OrgScore);
			Assert.Equal(a.OrgScores.Count, b.OrgScores.Count);
			for (int i = 0; i < a.OrgScores.Count; i++) {
				Assert.Equal(a.OrgScores[i].OrgId, b.OrgScores[i].OrgId);
				Assert.Equal(a.OrgScores[i].OrgScore, b.OrgScores[i].OrgScore);
			}
			Assert.Equal(a.OrgHandSize, b.OrgHandSize);
			Assert.Equal(a.CountryHandCount, b.CountryHandCount);
			Assert.Equal(a.CountryHandCapacity, b.CountryHandCapacity);
			Assert.Equal(a.CanStartCountryCardDraw, b.CanStartCountryCardDraw);
			Assert.Equal(a.TotalControl, b.TotalControl);
			Assert.Equal(a.OrgHand.Count, b.OrgHand.Count);
			for (int i = 0; i < a.OrgHand.Count; i++) { AssertCardEqual(a.OrgHand[i], b.OrgHand[i]); }
			Assert.Equal(a.CountryCardDrawChoices.Count, b.CountryCardDrawChoices.Count);
			for (int i = 0; i < a.CountryCardDrawChoices.Count; i++) {
				AssertDrawChoiceEqual(a.CountryCardDrawChoices[i], b.CountryCardDrawChoices[i]);
			}
			Assert.Equal(a.CharacterSlots.Count, b.CharacterSlots.Count);
			for (int i = 0; i < a.CharacterSlots.Count; i++) {
				Assert.Equal(a.CharacterSlots[i].RoleId, b.CharacterSlots[i].RoleId);
				Assert.Equal(a.CharacterSlots[i].SlotIndex, b.CharacterSlots[i].SlotIndex);
				Assert.Equal(a.CharacterSlots[i].IsAvailable, b.CharacterSlots[i].IsAvailable);
				Assert.Equal(a.CharacterSlots[i].CharacterId, b.CharacterSlots[i].CharacterId);
			}
			Assert.Equal(a.Countries.Count, b.Countries.Count);
			for (int i = 0; i < a.Countries.Count; i++) { AssertCountryEqual(a.Countries[i], b.Countries[i]); }
		}

		static void AssertOrdered(BotObservation obs) {
			for (int i = 1; i < obs.OrgHand.Count; i++) {
				Assert.True(obs.OrgHand[i - 1].SlotIndex <= obs.OrgHand[i].SlotIndex);
			}
			for (int i = 1; i < obs.Countries.Count; i++) {
				Assert.True(string.CompareOrdinal(obs.Countries[i - 1].CountryId, obs.Countries[i].CountryId) < 0);
			}
			for (int i = 1; i < obs.CountryCardDrawChoices.Count; i++) {
				Assert.True(obs.CountryCardDrawChoices[i - 1].ChoiceIndex <= obs.CountryCardDrawChoices[i].ChoiceIndex);
			}
			for (int i = 1; i < obs.OrgScores.Count; i++) {
				Assert.True(string.CompareOrdinal(obs.OrgScores[i - 1].OrgId, obs.OrgScores[i].OrgId) < 0);
			}
			foreach (var c in obs.Countries) {
				for (int i = 1; i < c.ControlByOrg.Count; i++) {
					Assert.True(string.CompareOrdinal(c.ControlByOrg[i - 1].OrgId, c.ControlByOrg[i].OrgId) < 0);
				}
				for (int i = 1; i < c.Hand.Count; i++) {
					Assert.True(c.Hand[i - 1].SlotIndex <= c.Hand[i].SlotIndex);
				}
				for (int i = 1; i < c.Characters.Count; i++) {
					Assert.True(string.CompareOrdinal(c.Characters[i - 1].CharacterId, c.Characters[i].CharacterId) < 0);
				}
				for (int i = 1; i < c.RivalCountryIds.Count; i++) {
					Assert.True(string.CompareOrdinal(c.RivalCountryIds[i - 1], c.RivalCountryIds[i]) < 0);
				}
			}
		}

		static int FindCountryEntity(World world, string countryId) {
			int[] required = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) {
					if (countries[i].CountryId == countryId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}

		static GameLogic BuildProvinceOccupationLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = MultiOrgTestSupport.HqA, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = MultiOrgTestSupport.HqB, DisplayName = "France", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = MultiOrgTestSupport.OrgA,
						DisplayName = "Illuminati",
						HqCountryId = MultiOrgTestSupport.HqA,
						InitialGold = 1000.0,
						BaseControl = 10,
						InitialAgentSlots = 1
					}
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 24, 720 },
				AutoSaveInterval = "monthly"
			};
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "prov_a", CountryId = MultiOrgTestSupport.HqA, Population = 100 },
					new ProvinceEntry { ProvinceId = "prov_b", CountryId = MultiOrgTestSupport.HqA, Population = 200 },
					new ProvinceEntry { ProvinceId = "prov_c", CountryId = MultiOrgTestSupport.HqA, Population = 300 },
					new ProvinceEntry { ProvinceId = "prov_d", CountryId = MultiOrgTestSupport.HqB, Population = 400 }
				}
			};
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition { ResourceId = "gold", DefaultInitialValue = 0.0 }
				}
			};
			var ctx = new GameLogicContext(
				new MultiOrgTestSupport.StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new MultiOrgTestSupport.StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new MultiOrgTestSupport.StaticConfig<CountryConfig>(countryConfig),
				new MultiOrgTestSupport.StaticConfig<GameSettings>(gameSettings),
				new MultiOrgTestSupport.StaticConfig<ResourceConfig>(resourceConfig),
				new MultiOrgTestSupport.StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: MultiOrgTestSupport.OrgA,
				province: new MultiOrgTestSupport.StaticConfig<ProvinceConfig>(provinceConfig));
			return new GameLogic(ctx);
		}

		[Fact]
		void observation_hides_other_orgs_private_state() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 11);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);

			// All world countries are visible; private state still means gold is own-org only.
			Assert.NotNull(obsA.GetCountry(MultiOrgTestSupport.ExtraCountry2));
			Assert.Contains(obsA.Countries, c => c.CountryId == MultiOrgTestSupport.ExtraCountry2);

			Assert.Equal(OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA), obsA.Gold);
			Assert.NotEqual(OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgB), obsA.Gold);
		}

		[Fact]
		void all_world_countries_expose_public_control_breakdown() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 12);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int e = logic.World.Create();
			logic.World.Add(e, new ControlEffect { OrgId = MultiOrgTestSupport.OrgB, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 20, EffectId = "test_control" });

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var view = obsA.GetCountry(MultiOrgTestSupport.ExtraCountry1);

			Assert.NotNull(view);
			Assert.Equal(0, view!.MyControl);
			Assert.Equal(20, view.TotalControl);
			Assert.Single(view.ControlByOrg);
			Assert.Equal(MultiOrgTestSupport.OrgB, view.ControlByOrg[0].OrgId);
			Assert.Equal(20, view.ControlByOrg[0].Control);
		}

		[Fact]
		void country_shows_full_public_control_breakdown() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 13);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int ceA = logic.World.Create();
			logic.World.Add(ceA, new ControlEffect { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 30, EffectId = "a" });
			int ceB = logic.World.Create();
			logic.World.Add(ceB, new ControlEffect { OrgId = MultiOrgTestSupport.OrgB, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 15, EffectId = "b" });

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var view = obsA.GetCountry(MultiOrgTestSupport.ExtraCountry1);

			Assert.NotNull(view);
			Assert.Equal(30, view!.MyControl);
			Assert.Equal(45, view.TotalControl);
			Assert.Equal(2, view.ControlByOrg.Count);
			Assert.Equal(MultiOrgTestSupport.OrgA, view.ControlByOrg[0].OrgId);
			Assert.Equal(30, view.ControlByOrg[0].Control);
			Assert.Equal(MultiOrgTestSupport.OrgB, view.ControlByOrg[1].OrgId);
			Assert.Equal(15, view.ControlByOrg[1].Control);
		}

		[Fact]
		void card_playability_matches_pipeline_validation() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 14, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			PutCountryCardInHand(logic, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.CountryCardActionId);

			var obsBefore = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var spendCard = obsBefore.OrgHand.First(c => c.ActionId == MultiOrgTestSupport.SpendGoldActionId);
			bool expectedSpendPlayable = ActionPlayability.Evaluate(logic.World, logic.ActionConfig, -1, MultiOrgTestSupport.SpendGoldActionId, MultiOrgTestSupport.OrgA, null, _resources, _relations);
			Assert.Equal(expectedSpendPlayable, spendCard.IsPlayable);
			Assert.True(spendCard.IsPlayable);

			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = -995.0 });
			logic.Update(0f);

			var obsAfter = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var countryCard = obsAfter.GetCountry(MultiOrgTestSupport.HqA)!.Hand.First(c => c.ActionId == MultiOrgTestSupport.CountryCardActionId);
			bool expectedCountryPlayable = ActionPlayability.Evaluate(logic.World, logic.ActionConfig, -1, MultiOrgTestSupport.CountryCardActionId, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA, _resources, _relations);
			Assert.Equal(expectedCountryPlayable, countryCard.IsPlayable);
			Assert.False(countryCard.IsPlayable);
		}

		[Fact]
		void observation_metrics_match_org_metrics() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 15);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int ce = logic.World.Create();
			logic.World.Add(ce, new ControlEffect { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 5, EffectId = "extra" });

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var expectedByCountry = OrgMetrics.GetControlByCountry(logic.World, MultiOrgTestSupport.OrgA);

			Assert.Equal(OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA), obs.Gold);
			Assert.Equal(OrgMetrics.GetTotalControl(logic.World, MultiOrgTestSupport.OrgA), obs.TotalControl);
			foreach (var c in obs.Countries) {
				int expected = expectedByCountry.TryGetValue(c.CountryId, out int v) ? v : 0;
				Assert.Equal(expected, c.MyControl);
			}
		}

		[Fact]
		void observation_collections_are_deterministically_ordered() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 16, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var obsA1 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var obsA2 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			AssertObservationsEqual(obsA1, obsA2);
			AssertOrdered(obsA1);

			var obsB1 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgB, logic.Resources, logic.Relations);
			var obsB2 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgB, logic.Resources, logic.Relations);
			AssertObservationsEqual(obsB1, obsB2);
			AssertOrdered(obsB1);
		}

		[Fact]
		void resident_characters_expose_only_own_org_opinion() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 17);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int char1Entity = logic.World.Create();
			logic.World.Add(char1Entity, new Character { CharacterId = "char1", CountryId = MultiOrgTestSupport.HqA, OrgId = "", RoleId = "diplomat", NamePartKeys = Array.Empty<string>() });
			int opA = logic.World.Create();
			logic.World.Add(opA, new ResourceOwner("char1", OwnerType.Character));
			logic.World.Add(opA, new Resource { ResourceId = $"opinion_{MultiOrgTestSupport.OrgA}", Value = 42 });
			int opB = logic.World.Create();
			logic.World.Add(opB, new ResourceOwner("char1", OwnerType.Character));
			logic.World.Add(opB, new Resource { ResourceId = $"opinion_{MultiOrgTestSupport.OrgB}", Value = 77 });

			int char2Entity = logic.World.Create();
			logic.World.Add(char2Entity, new Character { CharacterId = "char2", CountryId = MultiOrgTestSupport.HqA, OrgId = "", RoleId = "diplomat", NamePartKeys = Array.Empty<string>() });

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var country = obsA.GetCountry(MultiOrgTestSupport.HqA);

			Assert.NotNull(country);
			var view1 = country!.Characters.First(c => c.CharacterId == "char1");
			Assert.Equal(42, view1.OpinionOfMyOrg);
			var view2 = country.Characters.First(c => c.CharacterId == "char2");
			Assert.Equal(0, view2.OpinionOfMyOrg);
		}

		// Bespoke minimal config for effect-classification tests: a single org hand with
		// three cards, each wired to an effect (or lack thereof) exercising one branch of
		// BotObservation.ClassifyRaisesControl.
		const string ControlPosCardId = "control_pos_card";
		const string ControlNegCardId = "control_neg_card";
		const string UnrelatedCardId = "unrelated_card";

		static EffectConfig BuildClassificationEffectConfig() {
			return new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams { EffectId = "control_pos", EffectType = "ControlChange", Amount = 5 },
					new ControlChangeEffectParams { EffectId = "control_neg", EffectType = "ControlChange", Amount = -3 }
				}
			};
		}

		static GameLogic BuildClassificationLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "HQ", DisplayName = "HQ", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry { OrganizationId = "Illuminati", DisplayName = "Illuminati", HqCountryId = "HQ", InitialGold = 0, BaseControl = 10, InitialAgentSlots = 1 }
				}
			};
			var gameSettings = new GameSettings { StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly" };
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> { new ResourceDefinition { ResourceId = "gold", DefaultInitialValue = 0.0 } }
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 3 },
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 0 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = "Illuminati", ActionIds = new List<string> { ControlPosCardId, ControlNegCardId, UnrelatedCardId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = ControlPosCardId, OwnerType = "org", EffectIds = new List<string> { "control_pos" } },
					new ActionDefinition { ActionId = ControlNegCardId, OwnerType = "org", EffectIds = new List<string> { "control_neg" } },
					new ActionDefinition { ActionId = UnrelatedCardId, OwnerType = "org", EffectIds = new List<string>() }
				}
			};
			var effectConfig = BuildClassificationEffectConfig();

			var ctx = new GameLogicContext(
				new MultiOrgTestSupport.StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new MultiOrgTestSupport.StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new MultiOrgTestSupport.StaticConfig<CountryConfig>(countryConfig),
				new MultiOrgTestSupport.StaticConfig<GameSettings>(gameSettings),
				new MultiOrgTestSupport.StaticConfig<ResourceConfig>(resourceConfig),
				new MultiOrgTestSupport.StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				action: new MultiOrgTestSupport.StaticConfig<ActionConfig>(actionConfig),
				effect: new MultiOrgTestSupport.StaticConfig<EffectConfig>(effectConfig));

			var logic = new GameLogic(ctx);
			logic.Update(0f);
			return logic;
		}

		[Fact]
		void positive_control_change_effect_sets_raises_control_flag() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.Resources, logic.Relations, effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == ControlPosCardId);
			Assert.True(card.RaisesControl);
		}

		[Fact]
		void negative_control_change_effect_does_not_set_raises_control_flag() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.Resources, logic.Relations, effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == ControlNegCardId);
			Assert.False(card.RaisesControl);
		}

		[Fact]
		void card_with_no_effects_leaves_raises_control_false() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.Resources, logic.Relations, effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == UnrelatedCardId);
			Assert.False(card.RaisesControl);
		}

		static DateTime ReadCurrentTime(GS.Main.GameLogic logic) {
			int[] required = { ECS.TypeId<GameTime>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) { return arch.GetColumn<GameTime>()[0].CurrentTime; }
			}
			return default;
		}

		[Fact]
		void country_card_on_cooldown_is_unplayable_for_that_org_only() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 21, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			PutCountryCardInHand(logic, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.CountryCardActionId);
			PutCountryCardInHand(logic, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.CountryCardActionId);

			DateTime currentTime = ReadCurrentTime(logic);
			int cooldownEntity = logic.World.Create();
			logic.World.Add(cooldownEntity, new ActionCooldownState {
				OrgId = MultiOrgTestSupport.OrgA,
				ActionId = MultiOrgTestSupport.CountryCardActionId,
				EndTime = currentTime.AddDays(7)
			});

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var cardA = obsA.GetCountry(MultiOrgTestSupport.HqA)!.Hand.First(c => c.ActionId == MultiOrgTestSupport.CountryCardActionId);
			Assert.False(cardA.IsPlayable);

			// A different org holding the same ActionId in the same country is unaffected.
			var obsB = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgB, logic.Resources, logic.Relations);
			var cardB = obsB.GetCountry(MultiOrgTestSupport.HqA)!.Hand.First(c => c.ActionId == MultiOrgTestSupport.CountryCardActionId);
			Assert.True(cardB.IsPlayable);
		}

		[Fact]
		void observation_exposes_war_opponent_and_own_progress() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 31);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			DateTime currentTime = ReadCurrentTime(logic);
			Assert.True(Wars.DeclareWar(
				logic.World, logic.Resources, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, currentTime, out string? warId));
			Assert.False(string.IsNullOrEmpty(warId));
			ResourceMutations.TrySetValue(logic.Resources, logic.World, warId!, ResourceDefinitions.WarProgress, 25, out _);

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var attacker = obs.GetCountry(MultiOrgTestSupport.HqA);
			var defender = obs.GetCountry(MultiOrgTestSupport.HqB);
			var neutral = obs.GetCountry(MultiOrgTestSupport.ExtraCountry2);

			Assert.NotNull(attacker);
			Assert.NotNull(defender);
			Assert.NotNull(neutral);
			Assert.True(attacker!.IsAtWar);
			Assert.True(defender!.IsAtWar);
			Assert.False(neutral!.IsAtWar);
			Assert.Equal(MultiOrgTestSupport.HqB, attacker.WarOpponentCountryId);
			Assert.Equal(MultiOrgTestSupport.HqA, defender.WarOpponentCountryId);
			Assert.Equal("", neutral.WarOpponentCountryId);
			Assert.Equal(Wars.GetOwnWarProgress(logic.World, logic.Resources, MultiOrgTestSupport.HqA), attacker.OwnWarProgress);
			Assert.Equal(Wars.GetOwnWarProgress(logic.World, logic.Resources, MultiOrgTestSupport.HqB), defender.OwnWarProgress);
			Assert.Equal(25, attacker.OwnWarProgress);
			Assert.Equal(-25, defender.OwnWarProgress);
			Assert.Equal(0, neutral.OwnWarProgress);
		}

		[Fact]
		void observation_lists_rivals_ordinally() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 32);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			logic.Relations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.ExtraCountry2, RelationKind.Rival);
			logic.Relations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, RelationKind.Rival);
			logic.Relations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.ExtraCountry1, RelationKind.Friend);

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var view = obs.GetCountry(MultiOrgTestSupport.HqA);
			Assert.NotNull(view);

			var expected = logic.Relations.GetRelationsByCountryId(logic.World, MultiOrgTestSupport.HqA).Rivals;
			expected.Sort(StringComparer.Ordinal);
			Assert.Equal(expected, view!.RivalCountryIds.ToList());
			Assert.DoesNotContain(MultiOrgTestSupport.ExtraCountry1, view.RivalCountryIds);
			for (int i = 1; i < view.RivalCountryIds.Count; i++) {
				Assert.True(string.CompareOrdinal(view.RivalCountryIds[i - 1], view.RivalCountryIds[i]) < 0);
			}
		}

		[Fact]
		void observation_occupied_owned_province_count_matches_occupation_state() {
			var logic = BuildProvinceOccupationLogic();
			logic.Update(0f);

			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_a", MultiOrgTestSupport.HqB);
			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_b", MultiOrgTestSupport.HqB);
			ProvinceOccupationSystem.SetOccupier(logic.World, "prov_c", MultiOrgTestSupport.HqA);

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var britain = obs.GetCountry(MultiOrgTestSupport.HqA);
			var france = obs.GetCountry(MultiOrgTestSupport.HqB);
			Assert.NotNull(britain);
			Assert.NotNull(france);
			Assert.Equal(3, britain!.OwnedProvinceCount);
			Assert.Equal(2, britain.OccupiedOwnedProvinceCount);
			Assert.Equal(1, france!.OwnedProvinceCount);
			Assert.Equal(0, france.OccupiedOwnedProvinceCount);

			ProvinceOccupationSystem.ClearOccupier(logic.World, "prov_a");
			obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			britain = obs.GetCountry(MultiOrgTestSupport.HqA);
			Assert.NotNull(britain);
			Assert.Equal(3, britain!.OwnedProvinceCount);
			Assert.Equal(1, britain.OccupiedOwnedProvinceCount);
		}

		[Fact]
		void observation_marks_destroyed_countries() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 33);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int deadEntity = FindCountryEntity(logic.World, MultiOrgTestSupport.ExtraCountry2);
			Assert.True(deadEntity >= 0);
			logic.World.Add(deadEntity, new IsDestroyed());

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			Assert.True(obs.GetCountry(MultiOrgTestSupport.ExtraCountry2)!.IsDestroyed);
			Assert.False(obs.GetCountry(MultiOrgTestSupport.HqA)!.IsDestroyed);
			Assert.Empty(obs.GetCountry(MultiOrgTestSupport.ExtraCountry2)!.RivalCountryIds);
		}

		[Fact]
		void observation_exposes_country_score_and_combat_inputs() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 34);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.CountryScore, 123.5, OwnerType.Country);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Recruits, 40, OwnerType.Country);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Damage, 55, OwnerType.Country);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Durability, 66, OwnerType.Country);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.TroopsDamageBonusPercent, 12.5, OwnerType.Country);

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var view = obs.GetCountry(MultiOrgTestSupport.HqA);
			Assert.NotNull(view);
			Assert.Equal(logic.Resources.GetValue(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.CountryScore), view!.CountryScore);
			Assert.Equal(logic.Resources.GetValue(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Recruits), view.Recruits);
			Assert.Equal(logic.Resources.GetValue(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Damage), view.Damage);
			Assert.Equal(logic.Resources.GetValue(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Durability), view.Durability);
			Assert.Equal(
				logic.Resources.GetValue(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.TroopsDamageBonusPercent),
				view.TroopsDamageBonusPercent);
			Assert.Equal(123.5, view.CountryScore);
			Assert.Equal(40, view.Recruits);
			Assert.Equal(55, view.Damage);
			Assert.Equal(66, view.Durability);
			Assert.Equal(12.5, view.TroopsDamageBonusPercent);
		}

		[Fact]
		void observation_org_score_and_public_org_scores() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 35);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			logic.Resources.Set(logic.World, MultiOrgTestSupport.OrgA, ResourceDefinitions.OrgScore, 11.5, OwnerType.Org);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.OrgB, ResourceDefinitions.OrgScore, 22.25, OwnerType.Org);

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			Assert.Equal(logic.Resources.GetValue(logic.World, MultiOrgTestSupport.OrgA, ResourceDefinitions.OrgScore), obs.OrgScore);
			Assert.Equal(11.5, obs.OrgScore);

			Assert.Equal(2, obs.OrgScores.Count);
			Assert.Equal(MultiOrgTestSupport.OrgA, obs.OrgScores[0].OrgId);
			Assert.Equal(11.5, obs.OrgScores[0].OrgScore);
			Assert.Equal(MultiOrgTestSupport.OrgB, obs.OrgScores[1].OrgId);
			Assert.Equal(22.25, obs.OrgScores[1].OrgScore);
			Assert.True(string.CompareOrdinal(obs.OrgScores[0].OrgId, obs.OrgScores[1].OrgId) < 0);
		}

		[Fact]
		void observation_war_fields_deterministic_across_rebuilds() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 36);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			DateTime currentTime = ReadCurrentTime(logic);
			Assert.True(Wars.DeclareWar(
				logic.World, logic.Resources, MultiOrgTestSupport.HqA, MultiOrgTestSupport.HqB, currentTime, out string? warId));
			ResourceMutations.TrySetValue(logic.Resources, logic.World, warId!, ResourceDefinitions.WarProgress, 17, out _);
			logic.Relations.SetRelation(logic.World, MultiOrgTestSupport.HqA, MultiOrgTestSupport.ExtraCountry1, RelationKind.Rival);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.CountryScore, 90, OwnerType.Country);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.HqA, ResourceDefinitions.Recruits, 8, OwnerType.Country);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.OrgA, ResourceDefinitions.OrgScore, 3.5, OwnerType.Org);
			logic.Resources.Set(logic.World, MultiOrgTestSupport.OrgB, ResourceDefinitions.OrgScore, 4.5, OwnerType.Org);
			int deadEntity = FindCountryEntity(logic.World, MultiOrgTestSupport.ExtraCountry2);
			logic.World.Add(deadEntity, new IsDestroyed());

			var obs1 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			var obs2 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA, logic.Resources, logic.Relations);
			AssertObservationsEqual(obs1, obs2);
			AssertOrdered(obs1);
		}
	}
}
