using System.Collections.Generic;

namespace GS.Game.Configs {
	public class ProvinceConfig {
		public List<ProvinceEntry> Provinces { get; set; } = new List<ProvinceEntry>();

		public ProvinceEntry? FindByProvinceId(string provinceId) {
			foreach (var entry in Provinces) {
				if (entry.ProvinceId == provinceId) return entry;
			}
			return null;
		}

		public List<ProvinceEntry> FindByCountryId(string countryId) {
			var result = new List<ProvinceEntry>();
			foreach (var entry in Provinces) {
				if (entry.CountryId == countryId) result.Add(entry);
			}
			return result;
		}
	}

	public class ProvinceEntry {
		public string ProvinceId { get; set; } = "";
		public string CountryId { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public string GenerationMethod { get; set; } = "";
	}
}
