using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class EventNotificationDispatcherTests {
		static WarResolvedApplied SampleApplied(
			string attacker = "Attacker",
			string defender = "Defender",
			string winner = "Attacker",
			string loser = "Defender") {
			return new WarResolvedApplied {
				WarId = "war_1",
				AttackerCountryId = attacker,
				DefenderCountryId = defender,
				WinnerCountryId = winner,
				LoserCountryId = loser,
				Progress = 40,
				GoldTaken = 10,
				GoldRecipients = new List<WarGoldRecipientSnapshot>(),
				ControlDeltas = new List<WarControlDeltaSnapshot>(),
				TransferredProvinces = new List<WarProvinceTransferSnapshot>(),
				History = new List<WarProgressHistorySnapshot>(),
				Attacker = new WarSideStatsSnapshot { CountryId = attacker },
				Defender = new WarSideStatsSnapshot { CountryId = defender },
				Battles = new List<WarBattleRowSnapshot>()
			};
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = $"base_{orgId}_{countryId}"
			});
		}

		[Fact]
		void defaults_write_log_always_even_without_influence() {
			var world = new World();
			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, null, "Org", SampleApplied());

			Assert.True(decision.WriteLog);
			Assert.False(decision.Show);
			Assert.False(decision.Pause);
			Assert.Null(decision.Snapshot);
		}

		[Fact]
		void defaults_show_and_pause_only_with_influence() {
			var world = new World();
			AddControl(world, "Org", "Attacker", 4);

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, new EventNotificationSettings(), "Org", SampleApplied());

			Assert.True(decision.WriteLog);
			Assert.True(decision.Show);
			Assert.True(decision.Pause);
			Assert.NotNull(decision.Snapshot);
			Assert.True(decision.Snapshot!.ShouldPause);
			Assert.Equal("war_1", decision.Snapshot.WarId);
			Assert.Equal("Attacker", decision.Snapshot.WinnerCountryId);
		}

		[Fact]
		void write_action_log_false_suppresses_log_independently() {
			var world = new World();
			AddControl(world, "Org", "Attacker", 5);
			var settings = new EventNotificationSettings {
				Events = new List<EventNotificationEntry> {
					new EventNotificationEntry {
						EventType = "war_resolved",
						Pause = true,
						ShowWindow = true,
						WriteActionLog = false,
						PauseCondition = EventNotificationSettings.CreateGtControlZero(),
						ShowWindowCondition = EventNotificationSettings.CreateGtControlZero()
					}
				}
			};

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, settings, "Org", SampleApplied());

			Assert.False(decision.WriteLog);
			Assert.True(decision.Show);
			Assert.True(decision.Pause);
		}

		[Fact]
		void show_window_false_suppresses_window_and_pause() {
			var world = new World();
			AddControl(world, "Org", "Attacker", 5);
			var settings = new EventNotificationSettings {
				Events = new List<EventNotificationEntry> {
					new EventNotificationEntry {
						EventType = "war_resolved",
						Pause = true,
						ShowWindow = false,
						WriteActionLog = true,
						PauseCondition = EventNotificationSettings.CreateGtControlZero(),
						ShowWindowCondition = EventNotificationSettings.CreateGtControlZero()
					}
				}
			};

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, settings, "Org", SampleApplied());

			Assert.True(decision.WriteLog);
			Assert.False(decision.Show);
			Assert.False(decision.Pause);
			Assert.Null(decision.Snapshot);
		}

		[Fact]
		void pause_false_shows_without_pause() {
			var world = new World();
			AddControl(world, "Org", "Defender", 2);
			var settings = new EventNotificationSettings {
				Events = new List<EventNotificationEntry> {
					new EventNotificationEntry {
						EventType = "war_resolved",
						Pause = false,
						ShowWindow = true,
						WriteActionLog = true,
						PauseCondition = EventNotificationSettings.CreateGtControlZero(),
						ShowWindowCondition = EventNotificationSettings.CreateGtControlZero()
					}
				}
			};

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, settings, "Org", SampleApplied());

			Assert.True(decision.Show);
			Assert.False(decision.Pause);
			Assert.NotNull(decision.Snapshot);
			Assert.False(decision.Snapshot!.ShouldPause);
		}

		[Fact]
		void missing_entry_falls_back_to_war_resolved_defaults() {
			var world = new World();
			AddControl(world, "Org", "Attacker", 1);
			var settings = new EventNotificationSettings {
				Events = new List<EventNotificationEntry> {
					new EventNotificationEntry { EventType = "other_event" }
				}
			};

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, settings, "Org", SampleApplied());

			Assert.True(decision.WriteLog);
			Assert.True(decision.Show);
			Assert.True(decision.Pause);
		}

		[Fact]
		void null_settings_falls_back_to_defaults() {
			var world = new World();
			AddControl(world, "Org", "Attacker", 1);

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, null, "Org", SampleApplied());

			Assert.True(decision.WriteLog);
			Assert.True(decision.Show);
			Assert.True(decision.Pause);
		}

		[Fact]
		void present_entry_with_omitted_conditions_still_applies_influence_gate() {
			var world = new World();
			var settings = new EventNotificationSettings {
				Events = new List<EventNotificationEntry> {
					new EventNotificationEntry {
						EventType = "war_resolved",
						Pause = true,
						ShowWindow = true,
						WriteActionLog = true,
						PauseCondition = null,
						ShowWindowCondition = null,
						WriteActionLogCondition = null
					}
				}
			};

			EventNotificationDecision withoutInfluence = EventNotificationDispatcher.EvaluateWarResolved(
				world, settings, "Org", SampleApplied());
			Assert.True(withoutInfluence.WriteLog);
			Assert.False(withoutInfluence.Show);
			Assert.False(withoutInfluence.Pause);

			AddControl(world, "Org", "Defender", 3);
			EventNotificationDecision withInfluence = EventNotificationDispatcher.EvaluateWarResolved(
				world, settings, "Org", SampleApplied());
			Assert.True(withInfluence.Show);
			Assert.True(withInfluence.Pause);
		}

		[Fact]
		void influence_gate_uses_pre_peace_control_after_loser_cut_to_zero() {
			var world = new World();
			// Live world already reflects post-peace: player cut to 0 in the loser.
			WarResolvedApplied applied = SampleApplied();
			applied.ControlDeltas = new List<WarControlDeltaSnapshot> {
				new WarControlDeltaSnapshot {
					CountryId = "Defender",
					OrgId = "Org",
					Delta = -4,
					TotalAfter = 0
				}
			};

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, null, "Org", applied);

			Assert.True(decision.WriteLog);
			Assert.True(decision.Show);
			Assert.True(decision.Pause);
		}

		[Fact]
		void influence_gate_falls_back_to_winner_loser_when_attacker_defender_omitted() {
			var world = new World();
			AddControl(world, "Org", "France", 3);
			WarResolvedApplied applied = new WarResolvedApplied {
				WarId = "war_1",
				WinnerCountryId = "Great_Britain",
				LoserCountryId = "France",
				Progress = 40,
				GoldRecipients = new List<WarGoldRecipientSnapshot>(),
				ControlDeltas = new List<WarControlDeltaSnapshot>(),
				TransferredProvinces = new List<WarProvinceTransferSnapshot>(),
				History = new List<WarProgressHistorySnapshot>(),
				Battles = new List<WarBattleRowSnapshot>()
			};

			EventNotificationDecision decision = EventNotificationDispatcher.EvaluateWarResolved(
				world, null, "Org", applied);

			Assert.True(decision.WriteLog);
			Assert.True(decision.Show);
			Assert.True(decision.Pause);
			Assert.NotNull(decision.Snapshot);
		}
	}
}
