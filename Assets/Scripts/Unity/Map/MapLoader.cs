using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class MapLoader : MonoBehaviour {
		[SerializeField] Map _mapPrefab;
		[SerializeField] TextAsset _geoJsonAsset;
		[SerializeField] Texture2D _mapTexture;

		List<MapFeature> _features;

		public Map Load() {
			if (_features == null) {
				if (_geoJsonAsset == null) {
					Debug.LogError("MapLoader: GeoJSON asset not assigned.");
					return null;
				}
				_features = GeoJsonParser.Parse(_geoJsonAsset.text);
				Debug.Log($"MapLoader: parsed {_features.Count} features.");
			}

			var map = Instantiate(_mapPrefab);
			map.Initialize(_features, _mapTexture);
			return map;
		}
	}
}
