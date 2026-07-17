using System;
using System.Collections.Generic;
using System.Linq;
using GS.Configs;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotObservationTests {
		static void AssertCardEqual(BotCardView a, BotCardView b) {
			Assert.Equal(a.ActionId, b.ActionId);
			Assert.Equal(a.SlotIndex, b.SlotIndex);
			Assert.Equal(a.CountryId, b.CountryId);
			Assert.Equal(a.GoldCost, b.GoldCost);
			Assert.Equal(a.IsPlayable, b.IsPlayable);
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
		}

		static void AssertObservationsEqual(BotObservation a, BotObservation b) {
			Assert.Equal(a.OrgId, b.OrgId);
			Assert.Equal(a.CurrentDate, b.CurrentDate);
			Assert.Equal(a.Gold, b.Gold);
			Assert.Equal(a.OrgHandSize, b.OrgHandSize);
			Assert.Equal(a.TotalControl, b.TotalControl);
			Assert.Equal(a.DiscoveredCountryIds, b.DiscoveredCountryIds);
			Assert.Equal(a.OrgHand.Count, b.OrgHand.Count);
			for (int i = 0; i < a.OrgHand.Count; i++) { AssertCardEqual(a.OrgHand[i], b.OrgHand[i]); }
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
			}
		}

		[Fact]
		void observation_hides_other_orgs_private_state() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 11);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int e = logic.World.Create();
			logic.World.Add(e, new DiscoveredCountry { OrgId = MultiOrgTestSupport.OrgB, CountryId = MultiOrgTestSupport.ExtraCountry2 });

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);

			Assert.DoesNotContain(MultiOrgTestSupport.ExtraCountry2, obsA.DiscoveredCountryIds);
			Assert.Null(obsA.GetCountry(MultiOrgTestSupport.ExtraCountry2));
			Assert.DoesNotContain(obsA.Countries, c => c.CountryId == MultiOrgTestSupport.ExtraCountry2);

			Assert.Equal(OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgA), obsA.Gold);
			Assert.NotEqual(OrgMetrics.GetGold(logic.World, MultiOrgTestSupport.OrgB), obsA.Gold);
		}

		[Fact]
		void undiscovered_country_control_breakdown_is_hidden() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 12);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int e = logic.World.Create();
			logic.World.Add(e, new ControlEffect { OrgId = MultiOrgTestSupport.OrgB, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 20, EffectId = "test_control" });

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);

			Assert.Null(obsA.GetCountry(MultiOrgTestSupport.ExtraCountry1));
			Assert.DoesNotContain(obsA.Countries, c => c.CountryId == MultiOrgTestSupport.ExtraCountry1);
		}

		[Fact]
		void discovered_country_shows_full_public_control_breakdown() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 13);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int discEntity = logic.World.Create();
			logic.World.Add(discEntity, new DiscoveredCountry { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1 });
			int ceA = logic.World.Create();
			logic.World.Add(ceA, new ControlEffect { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 30, EffectId = "a" });
			int ceB = logic.World.Create();
			logic.World.Add(ceB, new ControlEffect { OrgId = MultiOrgTestSupport.OrgB, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 15, EffectId = "b" });

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
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

			var obsBefore = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
			var spendCard = obsBefore.OrgHand.First(c => c.ActionId == MultiOrgTestSupport.SpendGoldActionId);
			bool expectedSpendPlayable = ActionPlayability.Evaluate(logic.World, logic.ActionConfig, MultiOrgTestSupport.SpendGoldActionId, MultiOrgTestSupport.OrgA, null);
			Assert.Equal(expectedSpendPlayable, spendCard.IsPlayable);
			Assert.True(spendCard.IsPlayable);

			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = -995.0 });
			logic.Update(0f);

			var obsAfter = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
			var countryCard = obsAfter.GetCountry(MultiOrgTestSupport.HqA)!.Hand.First(c => c.ActionId == MultiOrgTestSupport.CountryCardActionId);
			bool expectedCountryPlayable = ActionPlayability.Evaluate(logic.World, logic.ActionConfig, MultiOrgTestSupport.CountryCardActionId, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA);
			Assert.Equal(expectedCountryPlayable, countryCard.IsPlayable);
			Assert.False(countryCard.IsPlayable);
		}

		[Fact]
		void observation_metrics_match_org_metrics() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants, rngSeed: 15);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int e = logic.World.Create();
			logic.World.Add(e, new DiscoveredCountry { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1 });
			int ce = logic.World.Create();
			logic.World.Add(ce, new ControlEffect { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1, Value = 5, EffectId = "extra" });

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
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

			int eA = logic.World.Create();
			logic.World.Add(eA, new DiscoveredCountry { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry1 });
			int eB = logic.World.Create();
			logic.World.Add(eB, new DiscoveredCountry { OrgId = MultiOrgTestSupport.OrgA, CountryId = MultiOrgTestSupport.ExtraCountry2 });

			var obsA1 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
			var obsA2 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
			AssertObservationsEqual(obsA1, obsA2);
			AssertOrdered(obsA1);

			var obsB1 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgB);
			var obsB2 = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgB);
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

			var obsA = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
			var country = obsA.GetCountry(MultiOrgTestSupport.HqA);

			Assert.NotNull(country);
			var view1 = country!.Characters.First(c => c.CharacterId == "char1");
			Assert.Equal(42, view1.OpinionOfMyOrg);
			var view2 = country.Characters.First(c => c.CharacterId == "char2");
			Assert.Equal(0, view2.OpinionOfMyOrg);
		}

		// Bespoke minimal config for effect-classification tests: a single org hand with
		// four cards, each wired to an effect (or lack thereof) exercising one branch of
		// BotObservation.ClassifyCard.
		const string DiscoverCardId = "discover_card";
		const string ControlPosCardId = "control_pos_card";
		const string ControlNegCardId = "control_neg_card";
		const string UnrelatedCardId = "unrelated_card";

		static EffectConfig BuildClassificationEffectConfig() {
			return new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" },
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
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 4 },
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 0 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = "Illuminati", ActionIds = new List<string> { DiscoverCardId, ControlPosCardId, ControlNegCardId, UnrelatedCardId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = DiscoverCardId, OwnerType = "org", EffectIds = new List<string> { "discover" } },
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
		void discover_country_effect_sets_discovers_country_flag_only() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == DiscoverCardId);
			Assert.True(card.DiscoversCountry);
			Assert.False(card.RaisesControl);
		}

		[Fact]
		void positive_control_change_effect_sets_raises_control_flag_only() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == ControlPosCardId);
			Assert.False(card.DiscoversCountry);
			Assert.True(card.RaisesControl);
		}

		[Fact]
		void negative_control_change_effect_does_not_set_raises_control_flag() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == ControlNegCardId);
			Assert.False(card.DiscoversCountry);
			Assert.False(card.RaisesControl);
		}

		[Fact]
		void card_with_no_effects_leaves_both_classification_flags_false() {
			var logic = BuildClassificationLogic();
			var effectConfig = BuildClassificationEffectConfig();
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", effectConfig);

			var card = obs.OrgHand.First(c => c.ActionId == UnrelatedCardId);
			Assert.False(card.DiscoversCountry);
			Assert.False(card.RaisesControl);
		}
	}
}
