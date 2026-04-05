using System.Collections.Generic;

namespace GS.Game.Configs {
	public class CountryConfig {
		public List<CountryEntry> Countries { get; set; } = new List<CountryEntry>();

		public CountryEntry? FindByCountryId(string countryId) {
			foreach (var entry in Countries) {
				if (entry.CountryId == countryId) return entry;
			}
			return null;
		}
	}

	public class CountryEntry {
		public string CountryId { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public List<string> MainMapFeatureIds { get; set; } = new List<string>();
		public List<string> SecondaryMapFeatureIds { get; set; } = new List<string>();
	}
}
