using System;
using System.Collections.Generic;
using GS.Game.Bots;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotOrchestratorTests {
		sealed class ThrowingFeature : IBotFeature {
			public string FeatureId => "throwing";
			public void Tick(IBotObservation observation, IBotCommandSink sink, Random rng) => throw new InvalidOperationException("boom");
		}

		[Fact]
		void throwing_feature_surfaces_bot_feature_exception_naming_org_and_feature() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA };
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: participants);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var sink = new BotCommandSink(MultiOrgTestSupport.OrgA, logic.Commands, null);
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { new ThrowingFeature() }, new Random(1), sink);

			var ex = Assert.Throws<BotFeatureException>(() => bot.ExecuteDecisionTick(logic.World, logic.ActionConfig));
			Assert.Equal(MultiOrgTestSupport.OrgA, ex.OrgId);
			Assert.Equal("throwing", ex.FeatureId);
			Assert.Contains(MultiOrgTestSupport.OrgA, ex.Message);
			Assert.Contains("throwing", ex.Message);
			Assert.IsType<InvalidOperationException>(ex.InnerException);
		}
	}
}
