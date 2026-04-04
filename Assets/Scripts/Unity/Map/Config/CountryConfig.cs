using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Map {
	[CreateAssetMenu(fileName = "CountryConfig", menuName = "GlobalStrategy/Country Config")]
	public class CountryConfig : ScriptableObject {
		public List<CountryEntry> Countries = new List<CountryEntry>();

		public CountryEntry FindByFeatureId(string mapFeatureId) {
			foreach (var entry in Countries) {
				if (entry.mainMapFeatureIds.Contains(mapFeatureId)) return entry;
				if (entry.secondaryMapFeatureIds.Contains(mapFeatureId)) return entry;
			}
			return null;
		}
	}
}
