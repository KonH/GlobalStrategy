using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DebugForceCompletionConditionTests {
		static readonly List<string> Participants = new List<string> {
			MultiOrgTestSupport.OrgA,
			MultiOrgTestSupport.OrgB
		};

		static GameLogic BuildLogic(CompletionConditionConfig? completionCondition = null, int maxControlPool = 100) {
			var logic = new GameLogic(MultiOrgTestSupport.BuildContext(
				Participants, rngSeed: 41, completionCondition: completionCondition, maxControlPool: maxControlPool));
			logic.Update(0f);
			return logic;
		}

		static int GetControl(World world, string orgId, string countryId) {
			int total = 0;
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] effects = archetype.GetColumn<ControlEffect>();
				for (int i = 0; i < archetype.Count; i++) {
					if (effects[i].OrgId == orgId && effects[i].CountryId == countryId) { total += effects[i].Value; }
				}
			}
			return total;
		}

		static void GiveControl(GameLogic logic, string orgId, string countryId, int value) {
			logic.Commands.Push(new ChangeControlCommand { OrgId = orgId, CountryId = countryId, Delta = value });
		}

		static CompletionConditionConfig TotalControlCondition(double threshold) => new CompletionConditionConfig {
			Type = "total_control",
			Value = threshold
		};

		static CompletionConditionConfig FullControlCondition(double count) => new CompletionConditionConfig {
			Type = "full_control_countries",
			Value = count
		};

		[Fact]
		void total_control_fills_countries_in_deterministic_order_when_room_is_free() {
			// 4 available countries * 100 pool = 400 capacity; threshold 0.5 -> 200 required.
			// OrgA already holds 10 in its own HQ (Great_Britain) from init's BaseControl seed,
			// so only 190 more is needed.
			GameLogic logic = BuildLogic(TotalControlCondition(0.5));

			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgA,
				ConditionType = "total_control",
				Value = 0.5
			});
			logic.Update(0f);

			// Ordinal country order: Austria, France, Great_Britain, Prussia — Austria is filled
			// to its 100 cap, France takes the remaining 90, Great_Britain/Prussia are untouched
			// (Great_Britain keeps only its pre-existing base-control value of 10).
			Assert.Equal(100, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry2));
			Assert.Equal(90, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqB));
			Assert.Equal(10, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA));
			Assert.Equal(0, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry1));
		}

		[Fact]
		void total_control_evicts_most_control_opponent_first_and_only_by_the_needed_amount() {
			// Keep OrgB's total (35 here + its own 10 base control at France = 45) below the
			// 50-required threshold so this setup alone can't auto-complete the game as OrgB;
			// OrgC is not a completion participant and is free to hold whatever it likes.
			GameLogic logic = BuildLogic(TotalControlCondition(0.125), maxControlPool: 100);
			GiveControl(logic, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.ExtraCountry2, 35);
			GiveControl(logic, MultiOrgTestSupport.OrgC, MultiOrgTestSupport.ExtraCountry2, 50);
			logic.Update(0f);

			// 0.125 * 400 capacity = 50 required; OrgA already has 10 (its own base control at
			// Great_Britain), so 40 more is needed, entirely from Austria (first in ordinal order).
			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgA,
				ConditionType = "total_control",
				Value = 0.125
			});
			logic.Update(0f);

			// Free room in Austria is 100-(35+50)=15, so 25 of deficit must be evicted. OrgC
			// (50) has more control than OrgB (35), so it is reduced first — and only by the 25
			// needed, leaving OrgB completely untouched.
			Assert.Equal(40, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry2));
			Assert.Equal(35, GetControl(logic.World, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.ExtraCountry2));
			Assert.Equal(25, GetControl(logic.World, MultiOrgTestSupport.OrgC, MultiOrgTestSupport.ExtraCountry2));
		}

		[Fact]
		void full_control_countries_zeroes_all_opponents_in_the_selected_country() {
			GameLogic logic = BuildLogic(FullControlCondition(1), maxControlPool: 100);
			GiveControl(logic, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.ExtraCountry2, 60);
			GiveControl(logic, MultiOrgTestSupport.OrgC, MultiOrgTestSupport.ExtraCountry2, 40);
			logic.Update(0f);

			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgA,
				ConditionType = "full_control_countries",
				Value = 1
			});
			logic.Update(0f);

			Assert.Equal(100, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry2));
			Assert.Equal(0, GetControl(logic.World, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.ExtraCountry2));
			Assert.Equal(0, GetControl(logic.World, MultiOrgTestSupport.OrgC, MultiOrgTestSupport.ExtraCountry2));
		}

		[Fact]
		void force_completion_condition_is_a_noop_once_already_satisfied() {
			// The real completion condition (0.9) is kept well out of reach so the game does not
			// auto-complete mid-test — only the debug-forced sub-threshold (0.3) is being probed
			// for idempotency here.
			GameLogic logic = BuildLogic(TotalControlCondition(0.9));
			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgA,
				ConditionType = "total_control",
				Value = 0.3
			});
			logic.Update(0f);
			int firstPassTotal =
				GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry2)
				+ GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqB)
				+ GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA)
				+ GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry1);

			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgA,
				ConditionType = "total_control",
				Value = 0.3
			});
			logic.Update(0f);
			int secondPassTotal =
				GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry2)
				+ GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqB)
				+ GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA)
				+ GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry1);

			Assert.Equal(120, firstPassTotal);
			Assert.Equal(firstPassTotal, secondPassTotal);
			Assert.False(logic.IsCompleted);
		}

		[Fact]
		void eviction_reduces_an_opponents_control_across_all_of_its_effect_entities_in_the_country() {
			// OrgB's own HQ (France) carries a "base_{orgId}" ControlEffect seeded at init
			// (10) *in addition to* whatever a later ChangeControlCommand accumulates under a
			// separate "permanent_{orgId}_{countryId}" effect (here +20, for 30 total). Eviction
			// must reduce OrgB's real total there (30), not just the "permanent_" entity (20).
			GameLogic logic = BuildLogic(FullControlCondition(4), maxControlPool: 100);
			GiveControl(logic, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry2, 100);
			GiveControl(logic, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqA, 90); // + pre-existing 10 base = 100
			GiveControl(logic, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.ExtraCountry1, 100);
			GiveControl(logic, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.HqB, 20); // + pre-existing 10 base = 30
			logic.Update(0f);

			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgA,
				ConditionType = "full_control_countries",
				Value = 4 // all 4 countries: forces the solver into OrgB's own HQ (France)
			});
			logic.Update(0f);

			Assert.Equal(100, GetControl(logic.World, MultiOrgTestSupport.OrgA, MultiOrgTestSupport.HqB));
			Assert.Equal(0, GetControl(logic.World, MultiOrgTestSupport.OrgB, MultiOrgTestSupport.HqB));
		}

		[Fact]
		void forcing_a_bot_org_to_the_condition_completes_the_game_as_a_player_loss() {
			GameLogic logic = BuildLogic(TotalControlCondition(0.5));

			logic.Commands.Push(new DebugForceCompletionConditionCommand {
				TargetOrgId = MultiOrgTestSupport.OrgB,
				ConditionType = "total_control",
				Value = 0.5
			});
			logic.Update(0f);

			Assert.True(logic.IsCompleted);
			Assert.Equal(MultiOrgTestSupport.OrgB, logic.VisualState.GameCompletion.WinnerOrganizationId);
			Assert.Equal(GameResult.Lose, logic.VisualState.GameCompletion.Result);
		}
	}
}
