using System.Collections.Generic;
using System.IO;
using GS.Game.Configs;
using GS.Game.E2E;

namespace GS.Unity.E2E {
	public static class E2EOrgCatalog {
		public static OrganizationConfig Load() {
			string path = E2EPaths.OrganizationsConfig;
			if (!File.Exists(path)) {
				return new OrganizationConfig();
			}
			return E2EJson.Deserialize<OrganizationConfig>(File.ReadAllText(path));
		}

		public static string DefaultOrgId() {
			var config = Load();
			return config.Organizations.Count > 0 ? config.Organizations[0].OrganizationId : "";
		}

		public static OrganizationEntry Find(string orgId) {
			return Load().FindById(orgId);
		}

		public static List<string> Ids() {
			var ids = new List<string>();
			foreach (var org in Load().Organizations) {
				ids.Add(org.OrganizationId);
			}
			return ids;
		}
	}
}
