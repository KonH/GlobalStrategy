namespace GS.Unity.UI {
	// Drives the refresh cadence for cold-panel pull projections (Docs/Specs/26_08_28_16_ui-refactoring
	// phase 2): project immediately on open or right after a command the owning window pushes
	// itself, otherwise re-project on a coarse wall-clock accumulator while the window stays open,
	// skipped entirely while the game is paused since nothing a pull projection reads can have
	// changed. Plain C# (not MonoBehaviour) so each window document just calls ShouldRefresh from
	// its own Update().
	public class PullRefreshTimer {
		const float DefaultIntervalSeconds = 0.25f;

		readonly float _intervalSeconds;
		float _accumulatedSeconds;
		bool _refreshRequested = true;

		public PullRefreshTimer(float intervalSeconds = DefaultIntervalSeconds) {
			_intervalSeconds = intervalSeconds > 0f ? intervalSeconds : DefaultIntervalSeconds;
		}

		// Call on window open and right after any command the window itself pushes - forces the
		// next ShouldRefresh call to return true regardless of elapsed time or pause state.
		public void RequestImmediate() {
			_refreshRequested = true;
		}

		// Call once per frame from the owning document's Update while the window is open.
		public bool ShouldRefresh(float deltaTime, bool isPaused) {
			if (_refreshRequested) {
				_refreshRequested = false;
				_accumulatedSeconds = 0f;
				return true;
			}
			if (isPaused) {
				return false;
			}
			_accumulatedSeconds += deltaTime;
			if (_accumulatedSeconds < _intervalSeconds) {
				return false;
			}
			_accumulatedSeconds = 0f;
			return true;
		}
	}
}
