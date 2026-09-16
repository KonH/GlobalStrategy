using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct CharacterSlot {
		[CharacterOwnerId] public string OwnerId;     // countryId or orgId
		[RoleId] public string RoleId;
		public int SlotIndex;
		public bool IsAvailable;   // true = ready-for-hire (player org only)
		[CharacterId(AllowEmpty = true)] public string CharacterId; // "" if no character assigned
	}
}
