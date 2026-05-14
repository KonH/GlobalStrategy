namespace GS.Game.Components {
	[Savable]
	public struct Character {
		public string CharacterId;
		public string CountryId;
		public string OrgId;       // empty string = country character
		public string RoleId;
		public string[] NamePartKeys;
	}
}
