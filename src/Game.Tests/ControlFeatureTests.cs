using System;
using System.Collections.Generic;
using GS.Configs;
using GS.Game.Bots;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class ControlFeatureTests {
		sealed class RecordingSink : IBotCommandSink {
			public List<(string ActionId, string CountryId)> Plays = new();
			public void PlayOrgCard(string actionId, int slotIndex) => Plays.Add((actionId, ""));
			public void PlayCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId) {
				Plays.Add((actionId, countryId));
			}
		}

		const string ControlCardId = "raise_control_card";
		const string OpinionCardId = "opinion_card";
		const string OrgDistractorCardId = "org_distractor_card";

		// Bespoke minimal config for priority-order tests: a free org distractor plus two
		// always-affordable country cards (a positive ControlChangeEffectParams card and an
		// OpinionModifierEffectParams distractor) dealt into every country hand from init.
		static GameLogic BuildPriorityLogic(double orgGold) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "HQ", DisplayName = "HQ", IsAvailable = true },
					new CountryEntry { CountryId = "Austria", DisplayName = "Austria", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry { OrganizationId = "Illuminati", DisplayName = "Illuminati", HqCountryId = "HQ", InitialGold = orgGold, BaseControl = 10, InitialAgentSlots = 1 }
				}
			};
			var gameSettings = new GameSettings { StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly" };
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> { new ResourceDefinition { ResourceId = "gold", DefaultInitialValue = 0.0 } }
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 1 },
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 2 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = "Illuminati", ActionIds = new List<string> { OrgDistractorCardId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = OrgDistractorCardId, OwnerType = "org" },
					new ActionDefinition { ActionId = ControlCardId, OwnerType = "country", DeckCopies = 1, EffectIds = new List<string> { "control_pos" } },
					new ActionDefinition { ActionId = OpinionCardId, OwnerType = "country", DeckCopies = 1, EffectIds = new List<string> { "opinion" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams { EffectId = "control_pos", EffectType = "ControlChange", Amount = 5 },
					new OpinionModifierEffectParams { EffectId = "opinion", EffectType = "OpinionModifier" }
				}
			};

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

		static void RunPassive(GameLogic logic, int tickCount) {
			logic.Update(0f);
			for (int tick = 0; tick < tickCount; tick++) { logic.Update(24f); }
		}

		static void RunWithBot(GameLogic logic, Bot bot, int tickCount) {
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			logic.Update(0f);
			for (int tick = 0; tick < tickCount; tick++) {
				bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
				logic.Update(24f);
			}
		}

		static readonly List<string> DivergenceParticipants = new List<string> { MultiOrgTestSupport.OrgA };

		// Bespoke config for the divergence/disabled/determinism tests below: a single org
		// with an empty org hand and one always-affordable positive ControlChangeEffectParams
		// country card. This lets ControlFeature diverge measurably from a passive run via
		// GetTotalControl — MultiOrgTestSupport's default action set has no control-raising
		// card unless includeCountryCard is set.
		static GameLogic BuildDivergenceLogic(int seed) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "HQ", DisplayName = "HQ", IsAvailable = true },
					new CountryEntry { CountryId = "Austria", DisplayName = "Austria", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry { OrganizationId = "Illuminati", DisplayName = "Illuminati", HqCountryId = "HQ", InitialGold = 1000.0, BaseControl = 10, InitialAgentSlots = 1 }
				}
			};
			var gameSettings = new GameSettings { StartYear = 1880, DefaultLocale = "en", SpeedMultipliers = new[] { 1, 24, 720 }, AutoSaveInterval = "monthly" };
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> { new ResourceDefinition { ResourceId = "gold", DefaultInitialValue = 0.0 } }
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 0 },
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = ControlCardId, OwnerType = "country", EffectIds = new List<string> { "control_pos" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams { EffectId = "control_pos", EffectType = "ControlChange", Amount = 5 }
				}
			};

			var ctx = new GameLogicContext(
				new MultiOrgTestSupport.StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new MultiOrgTestSupport.StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new MultiOrgTestSupport.StaticConfig<CountryConfig>(countryConfig),
				new MultiOrgTestSupport.StaticConfig<GameSettings>(gameSettings),
				new MultiOrgTestSupport.StaticConfig<ResourceConfig>(resourceConfig),
				new MultiOrgTestSupport.StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				action: new MultiOrgTestSupport.StaticConfig<ActionConfig>(actionConfig),
				effect: new MultiOrgTestSupport.StaticConfig<EffectConfig>(effectConfig),
				rngSeed: seed);

			var logic = new GameLogic(ctx);
			logic.Update(0f);
			return logic;
		}

		[Fact]
		void plays_control_change_card_over_opinion_card_when_eligible() {
			// Org distractor is playable but ControlFeature ignores org cards; Austria has both a
			// positive ControlChangeEffectParams card and an OpinionModifierEffectParams distractor.
			// Only the control-change card qualifies — proving baselineCardPlay's "any playable
			// card" behavior does not leak into this feature.
			var logic = BuildPriorityLogic(orgGold: 1000.0);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new ControlFeature(new Dictionary<string, double>(), 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
			Assert.Equal((ControlCardId, "Austria"), sink.Plays[0]);
		}

		[Fact]
		void ignores_removed_discovery_threshold_parameter() {
			// Legacy discoveredCountriesAvailableControl must be ignored; ControlFeature still
			// plays the first eligible RaisesControl country card.
			var logic = BuildPriorityLogic(orgGold: 1000.0);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new ControlFeature(new Dictionary<string, double> { ["discoveredCountriesAvailableControl"] = 0 }, 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
			Assert.Equal((ControlCardId, "Austria"), sink.Plays[0]);
		}

		[Fact]
		void plays_at_most_one_card_per_tick() {
			var logic = BuildPriorityLogic(orgGold: 1000.0);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new ControlFeature(new Dictionary<string, double>(), 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.True(sink.Plays.Count <= 1);
		}

		[Fact]
		void control_bot_changes_metrics_relative_to_passive_run() {
			const int seed = 2024;
			var passive = BuildDivergenceLogic(seed);
			RunPassive(passive, 60);

			var withBot = BuildDivergenceLogic(seed);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, withBot.Commands, null);
			var feature = new ControlFeature(new Dictionary<string, double>(), 100);
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(seed, MultiOrgTestSupport.OrgA), sink, withBot.EffectConfig);
			RunWithBot(withBot, bot, 60);

			Assert.NotEqual(OrgMetrics.GetTotalControl(passive.World, MultiOrgTestSupport.OrgA), OrgMetrics.GetTotalControl(withBot.World, MultiOrgTestSupport.OrgA));
		}

		[Fact]
		void disabled_feature_yields_run_identical_to_passive() {
			const int seed = 3033;
			var passive = BuildDivergenceLogic(seed);
			RunPassive(passive, 60);

			var withDisabledBot = BuildDivergenceLogic(seed);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, withDisabledBot.Commands, null);
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature>(), BotRng.Create(seed, MultiOrgTestSupport.OrgA), sink, withDisabledBot.EffectConfig);
			RunWithBot(withDisabledBot, bot, 60);

			foreach (string orgId in DivergenceParticipants) {
				Assert.Equal(OrgMetrics.GetGold(passive.World, orgId), OrgMetrics.GetGold(withDisabledBot.World, orgId));
				Assert.Equal(OrgMetrics.GetTotalControl(passive.World, orgId), OrgMetrics.GetTotalControl(withDisabledBot.World, orgId));
				Assert.Equal(OrgMetrics.GetControlByCountry(passive.World, orgId), OrgMetrics.GetControlByCountry(withDisabledBot.World, orgId));
			}
			Assert.Equal(passive.VisualState.Time.CurrentTime, withDisabledBot.VisualState.Time.CurrentTime);
		}

		[Fact]
		void same_seed_produces_identical_end_state_with_control_bot() {
			const int seed = 4044;

			GameLogic BuildAndRun() {
				var logic = BuildDivergenceLogic(seed);
				var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);
				var feature = new ControlFeature(new Dictionary<string, double>(), 100);
				var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(seed, MultiOrgTestSupport.OrgA), sink, logic.EffectConfig);
				RunWithBot(logic, bot, 60);
				return logic;
			}

			var logicA = BuildAndRun();
			var logicB = BuildAndRun();

			foreach (string orgId in DivergenceParticipants) {
				Assert.Equal(OrgMetrics.GetGold(logicA.World, orgId), OrgMetrics.GetGold(logicB.World, orgId));
				Assert.Equal(OrgMetrics.GetTotalControl(logicA.World, orgId), OrgMetrics.GetTotalControl(logicB.World, orgId));
				Assert.Equal(OrgMetrics.GetControlByCountry(logicA.World, orgId), OrgMetrics.GetControlByCountry(logicB.World, orgId));
			}
			Assert.Equal(logicA.VisualState.Time.CurrentTime, logicB.VisualState.Time.CurrentTime);
		}
	}
}
