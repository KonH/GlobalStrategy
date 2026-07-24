namespace GS.Game.Commands {
	// ConditionType mirrors GS.Game.Configs.CompletionConditionTypeParser's raw strings
	// ("total_control" | "full_control_countries") — kept as a plain string here so
	// Game.Commands does not need a project reference to Game.Configs.
	public struct DebugForceCompletionConditionCommand : ICommand {
		public string TargetOrgId;
		public string ConditionType;
		public double Value;
	}
}
