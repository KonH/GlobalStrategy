using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct Character {
		[CharacterId] public string CharacterId;
		[CountryId] public string CountryId;
		[OrgId(AllowEmpty = true)] public string OrgId;       // empty string = country character
		[RoleId] public string RoleId;
		public string[] NamePartKeys;
	}
}
