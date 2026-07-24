namespace GS.Game.Commands {
	public struct DebugChangeGoldCommand : ICommand {
		[OrgId] public string OrgId;
		public double Amount;
	}
}
