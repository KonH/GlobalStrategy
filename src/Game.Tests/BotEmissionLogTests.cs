using System;
using System.Collections.Generic;
using GS.Game.Bots;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotEmissionLogTests {
		sealed record EmissionEntry(string FeatureId, string ActionId, string CountryId, string Date, int Tick);

		sealed class ScriptedFeature : IBotFeature {
			readonly string _actionId;
			public string FeatureId { get; }

			public ScriptedFeature(string featureId, string actionId) {
				FeatureId = featureId;
				_actionId = actionId;
			}

			public void Tick(IBotObservation observation, IBotCommandSink sink, Random rng) => sink.PlayOrgCard(_actionId);
		}

		static (List<EmissionEntry> emissions, Bot bot, BotCommandSink sink) BuildHost(GameLogic logic, string orgId, IReadOnlyList<IBotFeature> features, string date, int tick) {
			var emissions = new List<EmissionEntry>();
			Bot bot = null!;
			BotEmissionCallback callback = (actionId, countryId) =>
				emissions.Add(new EmissionEntry(bot.CurrentFeatureId, actionId, countryId, date, tick));
			var sink = new BotCommandSink(orgId, logic.Commands, null, callback);
			bot = new Bot(orgId, features, new Random(1), sink);
			return (emissions, bot, sink);
		}

		[Fact]
		void sink_callback_fires_only_for_accepted_plays() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: new List<string> { MultiOrgTestSupport.OrgA });
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var calls = new List<string>();
			BotEmissionCallback callback = (actionId, countryId) => calls.Add(actionId);
			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null, callback);

			sink.BeginDecisionPhase();
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId);
			sink.PlayOrgCard(MultiOrgTestSupport.SpendGoldActionId); // duplicate — suppressed, no callback

			Assert.Single(calls);
		}

		[Fact]
		void emissions_are_stamped_with_current_feature_org_date_and_tick() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: new List<string> { MultiOrgTestSupport.OrgA });
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var features = new List<IBotFeature> {
				new ScriptedFeature("featureOne", MultiOrgTestSupport.SpendGoldActionId),
				new ScriptedFeature("featureTwo", MultiOrgTestSupport.DiscoverActionId)
			};
			var (emissions, bot, _) = BuildHost(logic, MultiOrgTestSupport.OrgA, features, "1880-02-14", 44);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);

			Assert.Equal(2, emissions.Count);
			Assert.Equal("featureOne", emissions[0].FeatureId);
			Assert.Equal(MultiOrgTestSupport.SpendGoldActionId, emissions[0].ActionId);
			Assert.Equal("1880-02-14", emissions[0].Date);
			Assert.Equal(44, emissions[0].Tick);
			Assert.Equal("featureTwo", emissions[1].FeatureId);
			Assert.Equal(MultiOrgTestSupport.DiscoverActionId, emissions[1].ActionId);
		}

		[Fact]
		void identical_decision_sequences_produce_element_wise_identical_logs() {
			var features = new List<IBotFeature> {
				new ScriptedFeature("featureOne", MultiOrgTestSupport.SpendGoldActionId),
				new ScriptedFeature("featureTwo", MultiOrgTestSupport.DiscoverActionId)
			};

			var ctx1 = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: new List<string> { MultiOrgTestSupport.OrgA });
			var logic1 = new GameLogic(ctx1);
			logic1.Update(0f);
			var (emissions1, bot1, _) = BuildHost(logic1, MultiOrgTestSupport.OrgA, features, "1880-01-01", 1);
			bot1.ExecuteDecisionTick(logic1.World, logic1.ActionConfig);

			var ctx2 = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: new List<string> { MultiOrgTestSupport.OrgA });
			var logic2 = new GameLogic(ctx2);
			logic2.Update(0f);
			var (emissions2, bot2, _) = BuildHost(logic2, MultiOrgTestSupport.OrgA, features, "1880-01-01", 1);
			bot2.ExecuteDecisionTick(logic2.World, logic2.ActionConfig);

			Assert.Equal(emissions1, emissions2);
		}
	}
}
