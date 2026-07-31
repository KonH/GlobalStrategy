using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class WarIconsProjectorTests {
		const string PlayerOrgId = "player";

		static int AddControl(World world, string countryId, int value, string orgId = PlayerOrgId) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = "test"
			});
			return entity;
		}

		static int AddWar(World world, string warId, double progress, bool includeProgress = true) {
			int entity = world.Create();
			world.Add(entity, new War { WarId = warId });
			if (includeProgress) {
				int progressEntity = world.Create();
				world.Add(progressEntity, new ResourceOwner(warId, OwnerType.War));
				world.Add(progressEntity, new Resource {
					ResourceId = ResourceDefinitions.WarProgress,
					Value = progress
				});
			}
			return entity;
		}

		static void SetWarProgress(World world, string warId, double progress) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == warId
						&& resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						resources[i].Value = progress;
						return;
					}
				}
			}
			throw new System.InvalidOperationException($"War progress resource for '{warId}' not found.");
		}

		static int AddParticipant(World world, string warId, WarParticipantKind kind, string countryId) {
			int entity = world.Create();
			world.Add(entity, new WarParticipant {
				WarId = warId,
				Kind = kind,
				CountryId = countryId
			});
			return entity;
		}

		static void AddCompleteWar(World world, string warId, string attackerId, string defenderId, double progress = 0) {
			AddWar(world, warId, progress);
			AddParticipant(world, warId, WarParticipantKind.Attacker, attackerId);
			AddParticipant(world, warId, WarParticipantKind.Defender, defenderId);
		}

		[Fact]
		void invalid_player_organization_produces_no_entries() {
			var world = new World();
			AddCompleteWar(world, "war_a", "attacker", "defender");
			AddControl(world, "attacker", 10);

			Assert.Empty(WarIconsProjector.Build(world, ""));
		}

		[Theory]
		[InlineData(0)]
		[InlineData(-1)]
		void absent_zero_or_negative_control_excludes_war(int value) {
			var world = new World();
			AddCompleteWar(world, "war_a", "attacker", "defender");
			AddControl(world, "attacker", 100, "other_org");
			if (value != 0) {
				AddControl(world, "attacker", value);
			}

			Assert.Empty(WarIconsProjector.Build(world, PlayerOrgId));
		}

		[Theory]
		[InlineData("attacker")]
		[InlineData("defender")]
		void positive_control_on_either_side_includes_war(string controlledCountryId) {
			var world = new World();
			AddCompleteWar(world, "war_a", "attacker", "defender", -2.5);
			AddControl(world, controlledCountryId, 1);

			WarIconEntryState entry = Assert.Single(WarIconsProjector.Build(world, PlayerOrgId));
			Assert.Equal("war_a", entry.WarId);
			Assert.Equal(-2.5, entry.Progress);
		}

		[Fact]
		void control_is_aggregated_before_strict_positive_filtering() {
			var world = new World();
			AddCompleteWar(world, "war_a", "attacker", "defender");
			AddControl(world, "attacker", 10);
			AddControl(world, "attacker", -10);

			Assert.Empty(WarIconsProjector.Build(world, PlayerOrgId));

			AddControl(world, "attacker", 1);
			Assert.Single(WarIconsProjector.Build(world, PlayerOrgId));
		}

		[Fact]
		void all_participants_affect_relevance_but_primary_flags_are_ordinal_and_war_is_deduplicated() {
			var world = new World();
			AddWar(world, "war_a", 25.25);
			AddParticipant(world, "war_a", WarParticipantKind.Attacker, "Zulu");
			AddParticipant(world, "war_a", WarParticipantKind.Attacker, "Alpha");
			AddParticipant(world, "war_a", WarParticipantKind.Attacker, "Zulu");
			AddParticipant(world, "war_a", WarParticipantKind.Defender, "Yankee");
			AddParticipant(world, "war_a", WarParticipantKind.Defender, "Bravo");
			AddControl(world, "Zulu", 5);
			AddControl(world, "Yankee", 5);

			WarIconEntryState entry = Assert.Single(WarIconsProjector.Build(world, PlayerOrgId));
			Assert.Equal("Alpha", entry.AttackerCountryId);
			Assert.Equal("Bravo", entry.DefenderCountryId);
			Assert.Equal(25.25, entry.Progress);
		}

		[Fact]
		void wars_are_sorted_by_ordinal_id_independent_of_creation_order() {
			var world = new World();
			AddCompleteWar(world, "war_z", "a_z", "d_z");
			AddCompleteWar(world, "war_a", "a_a", "d_a");
			AddControl(world, "d_z", 1);
			AddControl(world, "a_a", 1);

			List<WarIconEntryState> entries = WarIconsProjector.Build(world, PlayerOrgId);

			Assert.Collection(entries,
				entry => Assert.Equal("war_a", entry.WarId),
				entry => Assert.Equal("war_z", entry.WarId));
		}

		[Fact]
		void malformed_wars_and_orphan_participants_are_omitted() {
			var world = new World();
			AddWar(world, "missing_progress", 0, false);
			AddParticipant(world, "missing_progress", WarParticipantKind.Attacker, "a");
			AddParticipant(world, "missing_progress", WarParticipantKind.Defender, "d");
			AddWar(world, "missing_attacker", 0);
			AddParticipant(world, "missing_attacker", WarParticipantKind.Defender, "d");
			AddWar(world, "missing_defender", 0);
			AddParticipant(world, "missing_defender", WarParticipantKind.Attacker, "a");
			AddWar(world, "", 0);
			AddParticipant(world, "", WarParticipantKind.Attacker, "a");
			AddParticipant(world, "", WarParticipantKind.Defender, "d");
			AddParticipant(world, "orphan", WarParticipantKind.Attacker, "a");
			AddParticipant(world, "orphan", WarParticipantKind.Defender, "d");
			AddControl(world, "a", 100);
			AddControl(world, "d", 100);

			Assert.Empty(WarIconsProjector.Build(world, PlayerOrgId));
		}

		[Fact]
		void progress_and_removal_are_reflected_on_next_projection() {
			var world = new World();
			int warEntity = AddWar(world, "war_a", 1.5);
			AddParticipant(world, "war_a", WarParticipantKind.Attacker, "attacker");
			AddParticipant(world, "war_a", WarParticipantKind.Defender, "defender");
			int controlEntity = AddControl(world, "defender", 5);

			Assert.Equal(1.5, Assert.Single(WarIconsProjector.Build(world, PlayerOrgId)).Progress);

			SetWarProgress(world, "war_a", -7.25);
			Assert.Equal(-7.25, Assert.Single(WarIconsProjector.Build(world, PlayerOrgId)).Progress);

			world.Destroy(controlEntity);
			Assert.Empty(WarIconsProjector.Build(world, PlayerOrgId));

			AddControl(world, "defender", 5);
			Assert.Single(WarIconsProjector.Build(world, PlayerOrgId));
			world.Destroy(warEntity);
			Assert.Empty(WarIconsProjector.Build(world, PlayerOrgId));
		}

		[Fact]
		void state_notifies_only_when_entry_content_changes() {
			var state = new WarIconsState();
			int notifications = 0;
			state.PropertyChanged += (_, __) => notifications++;

			state.Set(new List<WarIconEntryState> {
				new WarIconEntryState("war_a", 1.5, "attacker", "defender")
			});
			Assert.Equal(1, notifications);

			state.Set(new List<WarIconEntryState> {
				new WarIconEntryState("war_a", 1.5, "attacker", "defender")
			});
			Assert.Equal(1, notifications);

			state.Set(new List<WarIconEntryState> {
				new WarIconEntryState("war_a", 2.5, "attacker", "defender")
			});
			state.Set(new List<WarIconEntryState> {
				new WarIconEntryState("war_a", 2.5, "other", "defender")
			});
			state.Set(new List<WarIconEntryState>());
			Assert.Equal(4, notifications);
		}
	}
}
