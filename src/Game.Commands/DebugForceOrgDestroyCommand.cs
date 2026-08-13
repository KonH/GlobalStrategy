namespace GS.Game.Commands {
	// Debug-only: simulates OrgDestroySystem's real destruction outcome for the
	// target org immediately, bypassing its usual gold/hand/control preconditions.
	public struct DebugForceOrgDestroyCommand : ICommand {
		[OrgId] public string TargetOrgId;
	}
}
