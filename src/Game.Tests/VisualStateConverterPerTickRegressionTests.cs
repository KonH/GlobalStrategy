using System;
using ECS;
using GS.Game.Components;
using GS.Main;
using Xunit;

using GS.Game.Systems;

namespace GS.Game.Tests {
	// Phase 2 acceptance test ("nothing between two frames is missed"): a single
	// VisualStateConverter.Update call with no window open must no longer touch the pulled
	// leaderboard/goals/selected-war/debug-availability projections (structurally guaranteed now -
	// those sub-states were removed from VisualState and their per-tick calls removed from
	// Update), while every edge-triggered observation still fires on the exact same tick it did
	// before the pull-model refactor.
	public class VisualStateConverterPerTickRegressionTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static int SeedGameTime(World world) {
			int entity = world.Create();
			world.Add(entity, new GameTime { CurrentTime = new DateTime(1880, 1, 1) });
			return entity;
		}

		static int SeedLocale(World world) {
			int entity = world.Create();
			world.Add(entity, new Locale { Value = "en" });
			return entity;
		}

		[Fact]
		void single_update_with_no_window_open_still_fires_every_edge_triggered_path() {
			var world = new World();
			int gameTimeEntity = SeedGameTime(world);
			int localeEntity = SeedLocale(world);
			const int orgEntity = -1;

			// UpdateLastFrameEffects - transient ResourceChange archetype, swept next tick.
			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceChange {
				EffectId = "test_effect", ResourceId = "gold", OwnerId = "Prussia", Amount = 5
			});

			// UpdateGameLog (control branch) - transient ControlEffectApplied archetype.
			int controlAppliedEntity = world.Create();
			world.Add(controlAppliedEntity, new ControlEffectApplied {
				OrgId = "OrgA", CountryId = "Prussia", Delta = 10, Total = 10
			});

			// UpdateProvinceOwnership - version-gated, always fires on the first tick it observes.
			int ownershipEntity = world.Create();
			world.Add(ownershipEntity, new ProvinceOwnership { ProvinceId = "Prussia__west", OwnerId = "Prussia" });

			// UpdateOrgDestroyedResults - Enqueue/AcknowledgeCurrent queue.
			int orgDestroyedEntity = world.Create();
			world.Add(orgDestroyedEntity, new OrgDestroyedApplied { OrganizationId = "OrgDead" });

			// UpdateGameLog (country-destroyed queue) - Enqueue/AcknowledgeCurrent queue.
			int countryDestroyedEntity = world.Create();
			world.Add(countryDestroyedEntity, new CountryDestroyedApplied { CountryId = "CountryDead" });

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.Single(state.LastFrameEffects.Effects);
			Assert.Equal("test_effect", state.LastFrameEffects.Effects[0].EffectId);

			Assert.Single(state.GameLog.Entries);
			Assert.Equal(GameLogEntryKind.Control, state.GameLog.Entries[0].Kind);
			Assert.Equal("OrgA", state.GameLog.Entries[0].OrgId);

			Assert.Equal("Prussia", state.ProvinceOwnership.OwnerByProvinceId["Prussia__west"]);

			Assert.True(state.OrgDestroyedResults.TryPeek(out var orgSnapshot));
			Assert.Equal("OrgDead", orgSnapshot!.OrganizationId);

			Assert.True(state.CountryDestroyedResults.TryPeek(out var countrySnapshot));
			Assert.Equal("CountryDead", countrySnapshot!.CountryId);
		}
	}
}
