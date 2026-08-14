using System;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class ActiveTaskEntryStateTests {
		[Fact]
		void equality_includes_tutorial_fields() {
			var rewards = Array.Empty<ActiveTaskRewardState>();
			var a = new ActiveTaskEntryState("t0", "n", "d", rewards, true, "time_panel");
			var same = new ActiveTaskEntryState("t0", "n", "d", rewards, true, "time_panel");
			var differentTutorial = new ActiveTaskEntryState("t0", "n", "d", rewards, false, "time_panel");
			var differentHighlight = new ActiveTaskEntryState("t0", "n", "d", rewards, true, "actions_button");

			Assert.True(StateEquality.ActiveTaskEntryStateEquals(a, same));
			Assert.False(StateEquality.ActiveTaskEntryStateEquals(a, differentTutorial));
			Assert.False(StateEquality.ActiveTaskEntryStateEquals(a, differentHighlight));
		}
	}
}
