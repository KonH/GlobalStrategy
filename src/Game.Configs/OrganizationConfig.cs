using System.Collections.Generic;

namespace GS.Game.Configs {
	public class OrganizationConfig {
		public List<OrganizationEntry> Organizations { get; set; } = new();

		public OrganizationEntry? FindByHqCountry(string countryId) {
			foreach (var entry in Organizations) {
				if (entry.HqCountryId == countryId) return entry;
			}
			return null;
		}

		public OrganizationEntry? FindById(string orgId) {
			foreach (var entry in Organizations) {
				if (entry.OrganizationId == orgId) return entry;
			}
			return null;
		}
	}

	public class OrganizationEntry {
		public string OrganizationId { get; set; } = "";
		public string DisplayName { get; set; } = "";
		public string HqCountryId { get; set; } = "";
		public double InitialGold { get; set; }
		public int BaseInfluence { get; set; } = 10;
		public int InitialAgentSlots { get; set; } = 0;
	}
}
