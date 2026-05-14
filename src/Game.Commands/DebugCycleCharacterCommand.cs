namespace GS.Game.Commands {
	public struct DebugCycleCharacterCommand : ICommand {
		public string OwnerId;
		public string RoleId;
		public int SlotIndex;
	}
}
