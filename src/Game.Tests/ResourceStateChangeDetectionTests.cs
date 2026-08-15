using System.Collections.Generic;
using GS.Game.Commands;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	// Regression coverage for a bug where VisualState.PlayerOrganization.Resources.PropertyChanged
	// stopped firing for gold value changes after the first tick, because
	// StateEquality.ResourceStateEntryEquals compared AnimatableDouble.Actual — a field on a
	// cached object VisualStateConverter reuses across ticks — against itself. The UI's gold
	// counter would then only refresh incidentally (e.g. via an unrelated RefreshCountryViews()
	// call from some other handler), leaving it stuck at a stale, animation-barrier-held value
	// until the game was saved/loaded and the cache was rebuilt from scratch.
	public class ResourceStateChangeDetectionTests {
		[Fact]
		void player_gold_change_fires_property_changed_on_every_tick_it_actually_changes() {
			var ctx = MultiOrgTestSupport.BuildContext();
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int changeCount = 0;
			logic.VisualState.PlayerOrganization.Resources.PropertyChanged += (_, __) => changeCount++;

			double goldBefore = FindGold(logic.VisualState.PlayerOrganization.Resources);

			// First gold-changing tick: must fire.
			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = 250.0 });
			logic.Update(0f);
			Assert.Equal(1, changeCount);
			Assert.Equal(goldBefore + 250.0, FindGold(logic.VisualState.PlayerOrganization.Resources));

			// A no-op tick (nothing changed) must not fire again.
			logic.Update(0f);
			Assert.Equal(1, changeCount);

			// A second, independent gold-changing tick must fire again — this is the exact case
			// the cached-AnimatableDouble self-comparison bug broke: the second change was never
			// detected because both the old and new ResourceStateEntry pointed at the same,
			// already-mutated AnimatableDouble.
			logic.Commands.Push(new DebugChangeGoldCommand { OrgId = MultiOrgTestSupport.OrgA, Amount = -100.0 });
			logic.Update(0f);
			Assert.Equal(2, changeCount);
			Assert.Equal(goldBefore + 150.0, FindGold(logic.VisualState.PlayerOrganization.Resources));
		}

		static double FindGold(CountryResourcesState state) {
			foreach (var entry in state.Resources) {
				if (entry.ResourceId == "gold") { return entry.Value.Actual; }
			}
			throw new KeyNotFoundException("gold resource not found in PlayerOrganization.Resources");
		}
	}
}
