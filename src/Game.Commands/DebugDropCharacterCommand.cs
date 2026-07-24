namespace GS.Game.Commands {
	public struct DebugDropCharacterCommand : ICommand {
		[CharacterOwnerId] public string OwnerId;
		[RoleId] public string RoleId;
		public int SlotIndex;
	}
}
