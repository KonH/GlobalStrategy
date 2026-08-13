using System;
using System.Collections.Generic;
using GS.Game.Bots;
using GS.Game.Commands;
using GS.Main;
using Xunit;

using GS.Game.Systems;

namespace GS.Game.Tests {
	public class BotIntervalGatingTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
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
		void repeated_calls_within_the_same_interval_emit_at_most_one_card_total() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 1);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(1, MultiOrgTestSupport.OrgA), sink, logic.Resources, logic.Relations);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);

			Assert.True(plays.Count <= 1);
		}

		[Fact]
		void call_after_interval_hours_elapse_may_act_again() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 2);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(
				MultiOrgTestSupport.OrgA,
				new List<IBotFeature> { feature },
				BotRng.Create(2, MultiOrgTestSupport.OrgA),
				sink,
				logic.Resources,
				logic.Relations,
				botDecisionIntervalHours: 4);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			int playsAfterFirst = plays.Count;
			Assert.Single(plays);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.Single(plays);

			logic.Update(4f);
			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.True(plays.Count >= playsAfterFirst);
		}

		[Fact]
		void pre_init_call_with_no_game_time_entity_does_not_throw_and_does_not_permanently_block() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 3);
			var logic = new GameLogic(ctx);

			var (sink, plays) = BuildTrackingSink(MultiOrgTestSupport.OrgA, logic.Commands);
			var feature = new BaselineCardPlayFeature(new Dictionary<string, double>());
			var bot = new Bot(MultiOrgTestSupport.OrgA, new List<IBotFeature> { feature }, BotRng.Create(3, MultiOrgTestSupport.OrgA), sink, logic.Resources, logic.Relations);

			var ex = Record.Exception(() => bot.ExecuteDecisionTick(logic.World, logic.ActionConfig));
			Assert.Null(ex);

			logic.Update(0f);

			bot.ExecuteDecisionTick(logic.World, logic.ActionConfig);
			Assert.Single(plays);
		}
	}
}
