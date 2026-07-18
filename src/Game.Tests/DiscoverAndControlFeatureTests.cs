using System;
using System.Collections.Generic;
using GS.Configs;
using GS.Game.Bots;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DiscoverAndControlFeatureTests {
		sealed class RecordingSink : IBotCommandSink {
			public List<(string ActionId, string CountryId)> Plays = new();
			public void PlayOrgCard(string actionId) => Plays.Add((actionId, ""));
			public void PlayCountryCard(string actionId, string countryId) => Plays.Add((actionId, countryId));
		}

		const string DiscoverCardId = "discover_country_card";
		const string ControlCardId = "raise_control_card";
		const string OpinionCardId = "opinion_card";

		// Bespoke minimal config for priority-order tests, modeled on
		// BaselineCardPlayTests.BuildScanOrderLogic: a single org card whose affordability
		// is toggled via orgGold, plus two always-affordable country cards (a positive
		// ControlChangeEffectParams card and an OpinionModifierEffectParams distractor)
		// dealt once "Austria" is discovered.
		static GameLogic BuildPriorityLogic(double orgGold, bool discoverAustria) {
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
					new OrgActionPool { OrgId = "Illuminati", ActionIds = new List<string> { DiscoverCardId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = DiscoverCardId, OwnerType = "org", EffectIds = new List<string> { "discover" },
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 100.0 } }
					},
					new ActionDefinition { ActionId = ControlCardId, OwnerType = "country", EffectIds = new List<string> { "control_pos" } },
					new ActionDefinition { ActionId = OpinionCardId, OwnerType = "country", EffectIds = new List<string> { "opinion" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" },
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

			if (discoverAustria) {
				int discEntity = logic.World.Create();
				logic.World.Add(discEntity, new DiscoveredCountry { OrgId = "Illuminati", CountryId = "Austria" });
			}

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
		// country card, dealt into an already-discovered country from the start. This lets
		// discoverAndControl's second scan branch (raise control) diverge measurably from a
		// passive run purely via GetTotalControl - MultiOrgTestSupport's default action set has
		// no control-raising card, so Gold (used by BaselineCardPlayTests) is not a usable
		// divergence signal for this feature.
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

			int discEntity = logic.World.Create();
			logic.World.Add(discEntity, new DiscoveredCountry { OrgId = "Illuminati", CountryId = "Austria" });

			return logic;
		}

		[Fact]
		void plays_discover_country_org_card_when_playable_and_no_control_card_eligible_yet() {
			// Discover card affordable (order 1); Austria not yet discovered so there is no
			// country hand to fall back to.
			var logic = BuildPriorityLogic(orgGold: 1000.0, discoverAustria: false);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new DiscoverAndControlFeature(new Dictionary<string, double>(), 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
			Assert.Equal((DiscoverCardId, ""), sink.Plays[0]);
		}

		[Fact]
		void plays_control_change_card_over_opinion_card_once_country_is_discovered() {
			// Discover card unaffordable (org hand yields nothing), Austria already discovered
			// with both a positive ControlChangeEffectParams card and an OpinionModifierEffectParams
			// distractor in hand. Only the control-change card qualifies (order 2) - proving
			// baselineCardPlay's "any playable card" behavior does not leak into this feature.
			var logic = BuildPriorityLogic(orgGold: 5.0, discoverAustria: true);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new DiscoverAndControlFeature(new Dictionary<string, double>(), 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
			Assert.Equal((ControlCardId, "Austria"), sink.Plays[0]);
		}

		[Fact]
		void plays_discover_card_over_control_card_when_below_threshold() {
			// Both a playable discover card and a playable control card are available;
			// default parameters (no threshold) must preserve discover-first ordering.
			var logic = BuildPriorityLogic(orgGold: 1000.0, discoverAustria: true);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new DiscoverAndControlFeature(new Dictionary<string, double>(), 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
			Assert.Equal((DiscoverCardId, ""), sink.Plays[0]);
		}

		[Fact]
		void plays_control_card_over_discover_card_once_threshold_is_met() {
			// Same setup as above, but with discoveredCountriesAvailableControl=0 the
			// single already-discovered country (Austria) meets the threshold, so the
			// bot should prefer raising control over discovering further.
			var logic = BuildPriorityLogic(orgGold: 1000.0, discoverAustria: true);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new DiscoverAndControlFeature(new Dictionary<string, double> { ["discoveredCountriesAvailableControl"] = 0 }, 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
			Assert.Equal((ControlCardId, "Austria"), sink.Plays[0]);
		}

		[Fact]
		void plays_at_most_one_card_per_tick() {
			var logic = BuildPriorityLogic(orgGold: 1000.0, discoverAustria: true);
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, "Illuminati", logic.EffectConfig);
			var sink = new RecordingSink();
			var feature = new DiscoverAndControlFeature(new Dictionary<string, double>(), 100);

			feature.Tick(obs, sink, new Random(1));

			Assert.True(sink.Plays.Count <= 1);
		}

		[Fact]
		void discover_and_control_bot_changes_metrics_relative_to_passive_run() {
			const int seed = 2024;
			var passive = BuildDivergenceLogic(seed);
			RunPassive(passive, 60);

			var withBot = BuildDivergenceLogic(seed);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, withBot.Commands, null);
			var feature = new DiscoverAndControlFeature(new Dictionary<string, double>(), 100);
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
		void same_seed_produces_identical_end_state_with_discover_and_control_bot() {
			const int seed = 4044;

			GameLogic BuildAndRun() {
				var logic = BuildDivergenceLogic(seed);
				var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);
				var feature = new DiscoverAndControlFeature(new Dictionary<string, double>(), 100);
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

