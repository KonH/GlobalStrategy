using System.Collections.Generic;
using UnityEngine;
using GS.Configs;
using GS.Core.Map;

namespace GS.Unity.Common {
	public class MapGeometryConfig : IConfigSource<List<MapFeature>> {
		readonly TextAsset _geoJsonAsset;

		public MapGeometryConfig(TextAsset geoJsonAsset) {
			_geoJsonAsset = geoJsonAsset;
		}

		public List<MapFeature> Load() {
			if (_geoJsonAsset == null) { return new List<MapFeature>(); }
			return GeoJsonParser.Parse(_geoJsonAsset.text);
		}
	}
}
