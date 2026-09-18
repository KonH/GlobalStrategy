using GS.Game.Components;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

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

		[Fact]
		void single_update_with_no_window_open_still_fires_every_edge_triggered_path() {
			TestWorld world = TestWorld.Create()
				.GameTime()
				.Locale()
				// UpdateLastFrameEffects - transient ResourceChange archetype, swept next tick.
				.Entity().With(new ResourceChange {
					EffectId = "test_effect", ResourceId = "gold", OwnerId = "Prussia", Amount = 5
				})
				// UpdateGameLog (control branch) - transient ControlEffectApplied archetype.
				.Entity().With(new ControlEffectApplied {
					OrgId = "OrgA", CountryId = "Prussia", Delta = 10, Total = 10
				})
				// UpdateProvinceOwnership - version-gated, always fires on the first tick it observes.
				.Province("Prussia__west", "Prussia")
				// UpdateOrgDestroyedResults - Enqueue/AcknowledgeCurrent queue.
				.Entity().With(new OrgDestroyedApplied { OrganizationId = "OrgDead" })
				// UpdateGameLog (country-destroyed queue) - Enqueue/AcknowledgeCurrent queue.
				.Entity().With(new CountryDestroyedApplied { CountryId = "CountryDead" });
			// No org is created, so OrgEntity stays at its -1 "no player organization" sentinel.

			VisualState state = new VisualStateProbe(resources: _resources, relations: _relations)
				.Update(world)
				.State;

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
