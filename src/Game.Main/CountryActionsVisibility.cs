namespace GS.Main {
	/// <summary>
	/// UI-visibility hint set by the Unity HUD (via GameLogic.CountryActionsVisibility) so
	/// VisualStateConverter can skip the expensive per-card ActionPlayability evaluation for
	/// the selected country's hand while the "actions" sub-panel is collapsed and nothing reads
	/// that detail. Defaults to true (full detail) so hosts that never touch this flag - the
	/// console runner, benchmarks, and the existing VisualStateConverter test suite - keep
	/// today's behavior unchanged.
	/// </summary>
	public sealed class CountryActionsVisibility {
		public bool ActionsPanelOpen { get; set; } = true;
	}
}
