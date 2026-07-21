namespace GS.Game.Configs {
	// Single source of truth for resource id strings stored in Resource.ResourceId /
	// ResourceLink.ResourceId. Collector classes keep their own Id constants (those name a
	// collector/formula, not a resource), but must reference these instead of redeclaring
	// their own resource id literals.
	public static class ResourceDefinitions {
		public const string Gold = "gold";
		public const string Population = "population";
		public const string CountryPopulation = "country_population";
		public const string CountryScore = "country_score";
		public const string OrgScore = "org_score";
		public const string Recruits = "recruits";
	}
}
