using System;
using System.Collections.Generic;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Main;
using Xunit;

using GS.Game.Systems;

namespace GS.Game.Tests {
	public class BotOrchestratorTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		sealed class ThrowingFeature : IBotFeature {
			public string FeatureId => "throwing";
			public void CollectProposals(IBotObservation observation, IList<BotPlayProposal> proposals, Random rng) =>
				throw new InvalidOperationException("boom");
		}

		sealed class StubProposalFeature : IBotFeature {
			readonly string _actionId;
			readonly double _delta;
			public string FeatureId { get; }

			public StubProposalFeature(string featureId, string actionId, double delta) {
				FeatureId = featureId;
				_actionId = actionId;
				_delta = delta;
			}

			public void CollectProposals(IBotObservation observation, IList<BotPlayProposal> proposals, Random rng) {
				proposals.Add(new BotPlayProposal {
					FeatureId = FeatureId,
					ActionId = _actionId,
					SlotIndex = 0,
					EstimatedDeltaOrgScore = _delta
				});
			}
		}

		static (BotCommandSink Sink, List<(string FeatureId, string ActionId)> Plays) BuildTrackingSink(
			string orgId,
			IWriteOnlyCommandAccessor commands,
			Func<string> currentFeatureId) {
			var plays = new List<(string, string)>();
			var sink = new BotCommandSink(orgId, commands, null, (actionId, countryId) => {
				plays.Add((currentFeatureId(), actionId));
			});
			return (sink, plays);
		}

		[Fact]
		void throwing_feature_surfaces_bot_feature_exception_naming_org_and_feature() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { new ThrowingFeature() }, new Random(1), sink, logic.Resources, logic.Relations);

			var ex = Assert.Throws<BotFeatureException>(() => bot.ExecuteDecisionTick(logic.World, logic.ActionConfig));
			Assert.Equal(MultiOrgTestSupport.OrgA, ex.OrgId);
			Assert.Equal("throwing", ex.FeatureId);
			Assert.Contains(MultiOrgTestSupport.OrgA, ex.Message);
			Assert.Contains("throwing", ex.Message);
			Assert.IsType<InvalidOperationException>(ex.InnerException);
		}

		[Fact]
		void higher_arbitration_score_wins_and_emits_exactly_one_play() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			Bot bot = null!;
			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands, () => bot.CurrentFeatureId);
			var features = new List<IBotFeature> {
				new StubProposalFeature("low", MultiOrgTestSupport.SpendGoldActionId, 1.0),
				new StubProposalFeature("high", MultiOrgTestSupport.SampleOrgActionId, 5.0)
			};
			bot = new Bot(MultiOrgTestSupport.OrgA, features, new Random(1), sink, logic.Resources, logic.Relations);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);

			Assert.Single(plays);
			Assert.Equal(("high", MultiOrgTestSupport.SampleOrgActionId), plays[0]);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.Single(plays);
		}
	}
}
