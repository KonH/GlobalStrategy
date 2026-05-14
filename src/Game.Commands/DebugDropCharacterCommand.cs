namespace GS.Game.Commands {
	public struct DebugDropCharacterCommand : ICommand {
		public string OwnerId;
		public string RoleId;
		public int SlotIndex;
	}
}
