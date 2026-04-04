using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Map {
	[CreateAssetMenu(fileName = "MapFeatureConfig", menuName = "GlobalStrategy/Map Feature Config")]
	public class MapFeatureConfig : ScriptableObject {
		public List<MapFeatureEntry> Features = new List<MapFeatureEntry>();

		public MapFeatureEntry Find(string geoJsonId) {
			foreach (var entry in Features)
				if (entry.geoJsonId == geoJsonId) return entry;
			return null;
		}
	}
}
