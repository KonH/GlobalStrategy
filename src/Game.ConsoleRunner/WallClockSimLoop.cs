using System;
using ECS.Viewer;
using GS.Game.Bots;

namespace GS.Game.ConsoleRunner {
	public sealed class WallClockSimLoop {
		public const double TickHz = 30.0;

		public double ElapsedSeconds { get; private set; }

		public void Tick(double elapsedSeconds, Action update, SimulationMarshal marshal, PauseToken pauseToken) {
			if (update == null) {
				throw new ArgumentNullException(nameof(update));
			}
			if (marshal == null) {
				throw new ArgumentNullException(nameof(marshal));
			}
			if (pauseToken == null) {
				throw new ArgumentNullException(nameof(pauseToken));
			}

			marshal.Drain();
			ElapsedSeconds += elapsedSeconds;
			if (!pauseToken.IsPaused) {
				update();
			}
		}

		public void Tick(double elapsedSeconds, BotSession session, SimulationMarshal marshal, PauseToken pauseToken) {
			Tick(elapsedSeconds, () => session.Update((float)elapsedSeconds), marshal, pauseToken);
		}

		public void Run(BotSession session, SimulationMarshal marshal, PauseToken pauseToken) {
			var interval = TimeSpan.FromSeconds(1.0 / TickHz);
			var lastTick = DateTime.UtcNow;
			while (true) {
				var now = DateTime.UtcNow;
				double elapsedSeconds = (now - lastTick).TotalSeconds;
				lastTick = now;
				Tick(elapsedSeconds, session, marshal, pauseToken);
				TimeSpan sleep = interval - (DateTime.UtcNow - now);
				if (sleep > TimeSpan.Zero) {
					System.Threading.Thread.Sleep(sleep);
				}
			}
		}
	}
}
