using System;
using System.Collections.Generic;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotDayGatingTests {
		sealed class NullCommandAccessor : IWriteOnlyCommandAccessor {
			public void Push<TCommand>(TCommand cmd) where TCommand : ICommand { }
		}

		static readonly List<string> Participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };

		static (BotCommandSink Sink, List<(string ActionId, string CountryId)> Plays) BuildTrackingSink(string orgId, IWriteOnlyCommandAccessor commands) {
			var plays = new List<(string, string)>();
			var sink = new BotCommandSink(orgId, commands, null, (actionId, countryId) => plays.Add((actionId, countryId)));
			return (sink, plays);
		}

		[Fact]
		void repeated_calls_within_the_same_simulated_day_emit_at_most_one_card_total() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 1);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(1, MultiOrgTestSupport.OrgA), sink);

			// Same simulated day for all three calls - no time advance in between.
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);

			Assert.True(plays.Count <= 1);
		}

		[Fact]
		void call_after_day_advances_may_act_again() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 2);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(2, MultiOrgTestSupport.OrgA), sink);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			int playsAfterDay1 = plays.Count;
			Assert.Single(plays);

			// Same day again - gated, no additional play.
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.Single(plays);

			// Advance one simulated day, then the bot may act again.
			logic.Update(24f);
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.True(plays.Count >= playsAfterDay1);
		}

		[Fact]
		void pre_init_call_with_no_game_time_entity_does_not_throw_and_does_not_permanently_block() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 3);
			var logic = new GameLogic(ctx);

			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(3, MultiOrgTestSupport.OrgA), sink);

			// World has no GameTime entity yet (Update has never run) - ReadCurrentDate must
			// return default rather than throw, and the gate must still let this first-ever
			// call through (no prior recorded date to compare against).
			var ex = Record.Exception(() => bot.ExecuteDecisionTick(logic.World, logic.ActionConfig));
			Assert.Null(ex);

			logic.Update(0f);

			// The first real decision after init must still be able to act - not permanently
			// blocked by the pre-init gate having recorded a "day" (DateTime default) that
			// would otherwise wrongly match every future date's .Date comparison.
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.Single(plays);
		}
	}
}
