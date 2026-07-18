namespace GS.Game.Components {
	public struct ControlEffectApplied {
		public string OrgId;
		public string CountryId;
		public double Delta;
		public double Total;
	}

	public struct OpinionEffectApplied {
		public string OrgId;
		public string CharacterId;
		public double Delta;
		public double Total; // raw, unclamped — VisualStateConverter applies the display clamp
	}

	public struct DiscoveryApplied {
		public string OrgId;
		public string CountryId;
	}

	public struct RoleChangeApplied {
		public string CountryId; // set for country-government roles, "" for org roles
		public string OrgId;     // set for org roles, "" for country-government roles
		public string RoleId;
		public string CharacterId;
	}
}
