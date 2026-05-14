namespace GS.Game.Components {
	[Savable]
	public struct CharacterSlot {
		public string OwnerId;     // countryId or orgId
		public string RoleId;
		public int SlotIndex;
		public bool IsAvailable;   // true = ready-for-hire (player org only)
		public string CharacterId; // "" if no character assigned
	}
}
