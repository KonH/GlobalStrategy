using System.Collections.Generic;
using GS.Game.Common;

namespace GS.Game.Systems {
	public static class BorderClassifier {
		public static bool ShouldRenderBoundary(
			string ownerIdA,
			string ownerIdB,
			MapLens lens,
			IReadOnlyDictionary<string, string> topOrgIdByCountryId) {
			if (lens == MapLens.Org) {
				string orgA = topOrgIdByCountryId.TryGetValue(ownerIdA, out var a) ? a : "";
				string orgB = topOrgIdByCountryId.TryGetValue(ownerIdB, out var b) ? b : "";
				if (orgA != "" && orgB != "") {
					return orgA != orgB;
				}
			}
			return ownerIdA != ownerIdB;
		}
	}
}
