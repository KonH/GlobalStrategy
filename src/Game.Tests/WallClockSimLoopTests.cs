using ECS.Viewer;
using GS.Game.ConsoleRunner;
using Xunit;

namespace GS.Game.Tests {
	public class WallClockSimLoopTests {
		[Fact]
		void freeze_skips_update_drain_always_runs_elapsed_accumulates() {
			var loop = new WallClockSimLoop();
			var marshal = new SimulationMarshal();
			var pause = new PauseToken();
			int updates = 0;
			bool drained = false;

			marshal.Enqueue(() => drained = true);
			loop.Tick(0.5, () => updates++, marshal, pause);

			Assert.True(drained);
			Assert.Equal(1, updates);
			Assert.Equal(0.5, loop.ElapsedSeconds);

			pause.IsPaused = true;
			drained = false;
			marshal.Enqueue(() => drained = true);
			loop.Tick(0.25, () => updates++, marshal, pause);

			Assert.True(drained);
			Assert.Equal(1, updates);
			Assert.Equal(0.75, loop.ElapsedSeconds);
		}
	}
}
