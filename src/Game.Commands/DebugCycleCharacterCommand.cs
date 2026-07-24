namespace GS.Game.Commands {
	public struct DebugCycleCharacterCommand : ICommand {
		[CharacterOwnerId] public string OwnerId;
		[RoleId] public string RoleId;
		public int SlotIndex;
	}
}
