using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Map {
	[CreateAssetMenu(fileName = "OrgVisualConfig", menuName = "GlobalStrategy/Org Visual Config")]
	public class OrgVisualConfig : ScriptableObject {
		public List<OrgVisualEntry> Entries = new List<OrgVisualEntry>();

		public OrgVisualEntry Find(string orgId) {
			foreach (var entry in Entries) {
				if (entry.orgId == orgId) {
					return entry;
				}
			}
			return null;
		}
	}
}
