namespace GS.Main {
	/// <summary>
	/// UI-visibility hint set by the Unity HUD (via GameLogic.DebugOrgCardVisibility) so
	/// VisualStateConverter can skip the expensive per-card ActionPlayability evaluation for the
	/// debug menu's "My org" / "Selected org" deck and hand listings while their sub-sections are
	/// collapsed and nothing reads that detail. Defaults to false (cheap placeholder entries) so
	/// hosts that never touch this flag - the console runner, benchmarks, and the existing
	/// VisualStateConverter test suite - never pay for detail nobody displays.
	/// </summary>
	public sealed class DebugOrgCardVisibility {
		public bool MyOrgDeckOpen { get; set; }
		public bool MyOrgHandOpen { get; set; }
		public bool SelectedOrgDeckOpen { get; set; }
		public bool SelectedOrgHandOpen { get; set; }
	}
}
