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
	public class BaselineCardPlayTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		sealed class RecordingSink : IBotCommandSink {
			public List<(string ActionId, string CountryId)> Plays = new();
			public void PlayOrgCard(string actionId) => Plays.Add((actionId, ""));
			public void PlayCountryCard(string actionId, string countryId) => Plays.Add((actionId, countryId));
		}

		static readonly List<string> Participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };

		static GameLogic BuildLogic(int seed) {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: seed);
			return new GameLogic(ctx);
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

		static void AssertIdenticalEndState(GameLogic expected, GameLogic actual, IEnumerable<string> orgIds) {
			foreach (string orgId in orgIds) {
				Assert.Equal(OrgMetrics.GetGold(expected.World, orgId), OrgMetrics.GetGold(actual.World, orgId));
				Assert.Equal(OrgMetrics.GetTotalControl(expected.World, orgId), OrgMetrics.GetTotalControl(actual.World, orgId));
				Assert.Equal(OrgMetrics.GetControlByCountry(expected.World, orgId), OrgMetrics.GetControlByCountry(actual.World, orgId));
			}
			Assert.Equal(expected.VisualState.Time.CurrentTime, actual.VisualState.Time.CurrentTime);
		}

		// Bespoke minimal config for order-of-scan tests: single org card whose affordability
		// can be toggled via InitialGold, plus a country card cheap enough to always be affordable,
		// discovered in two countries ("Austria" sorts before "Prussia" ordinally).
		static GameLogic BuildScanOrderLogic(double orgGold) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "HQ", DisplayName = "HQ", IsAvailable = true },
					new CountryEntry { CountryId = "Austria", DisplayName = "Austria", IsAvailable = true },
					new CountryEntry { CountryId = "Prussia", DisplayName = "Prussia", IsAvailable = true }
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
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = "Illuminati", ActionIds = new List<string> { "expensive_org_card" } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = "expensive_org_card", OwnerType = "org",
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 100.0 } }
					},
					new ActionDefinition {
						ActionId = "cheap_country_card", OwnerType = "country",
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 1.0 } }
					}
				}
			};

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				action: new StaticConfig<ActionConfig>(actionConfig),
				effect: new StaticConfig<EffectConfig>(new EffectConfig()));

			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int e1 = logic.World.Create();
			logic.World.Add(e1, new DiscoveredCountry { OrgId = "Illuminati", CountryId = "Austria" });
			int e2 = logic.World.Create();
			logic.World.Add(e2, new DiscoveredCountry { OrgId = "Illuminati", CountryId = "Prussia" });

			return logic;
		}

		[Fact]
		void baseline_bot_changes_metrics_relative_to_passive_run() {
			const int seed = 999;
			var passive = BuildLogic(seed);
			RunPassive(passive, 60);

			var withBot = BuildLogic(seed);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, withBot.Commands, null);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(seed, MultiOrgTestSupport.OrgA), sink);
			RunWithBot(withBot, bot, 60);

			Assert.NotEqual(OrgMetrics.GetGold(passive.World, MultiOrgTestSupport.OrgA), OrgMetrics.GetGold(withBot.World, MultiOrgTestSupport.OrgA));
		}

		[Fact]
		void baseline_plays_at_most_one_card_per_tick() {
			var logic = BuildLogic(1);
			logic.Update(0f);

			var obs = BotObservation.Build(logic.World, logic.ActionConfig, MultiOrgTestSupport.OrgA);
			var sink = new RecordingSink();
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			feature.Tick(obs, sink, new Random(1));

			Assert.Single(sink.Plays);
		}

		[Fact]
		void baseline_scans_org_hand_then_countries_in_documented_order() {
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());

			// Org hand playable -> the org-hand card is chosen.
			var logicPlayable = BuildScanOrderLogic(orgGold: 1000.0);
			var obsPlayable = BotObservation.Build(logicPlayable.World, logicPlayable.ActionConfig, "Illuminati");
			var sinkPlayable = new RecordingSink();
			feature.Tick(obsPlayable, sinkPlayable, new Random(1));
			Assert.Single(sinkPlayable.Plays);
			Assert.Equal(("expensive_org_card", ""), sinkPlayable.Plays[0]);

			// Org hand unplayable -> the ordinal-first discovered country's card is chosen.
			var logicUnplayable = BuildScanOrderLogic(orgGold: 5.0);
			var obsUnplayable = BotObservation.Build(logicUnplayable.World, logicUnplayable.ActionConfig, "Illuminati");
			var sinkUnplayable = new RecordingSink();
			feature.Tick(obsUnplayable, sinkUnplayable, new Random(1));
			Assert.Single(sinkUnplayable.Plays);
			Assert.Equal(("cheap_country_card", "Austria"), sinkUnplayable.Plays[0]);
		}

		[Fact]
		void min_gold_reserve_above_available_gold_prevents_all_plays() {
			const int seed = 1234;
			var passive = BuildLogic(seed);
			RunPassive(passive, 60);

			var withBot = BuildLogic(seed);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, withBot.Commands, null);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double> { ["minGoldReserve"] = 1_000_000_000.0 });
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(seed, MultiOrgTestSupport.OrgA), sink);
			RunWithBot(withBot, bot, 60);

			AssertIdenticalEndState(passive, withBot, Participants);
		}

		[Fact]
		void disabled_feature_yields_run_identical_to_passive() {
			const int seed = 4242;
			var passive = BuildLogic(seed);
			RunPassive(passive, 60);

			var withDisabledBot = BuildLogic(seed);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, withDisabledBot.Commands, null);
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature>(), BotRng.Create(seed, MultiOrgTestSupport.OrgA), sink);
			RunWithBot(withDisabledBot, bot, 60);

			AssertIdenticalEndState(passive, withDisabledBot, Participants);
		}
	}
}
