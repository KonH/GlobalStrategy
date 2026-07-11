using System.Collections.Generic;
using UnityEngine;
using VContainer;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class MapLoader : MonoBehaviour {
		[SerializeField] Map _mapPrefab;
		[SerializeField] TextAsset _provinceGeoJsonAsset;
		[SerializeField] Texture2D[] _mapTiles;
		[SerializeField] int _tileCols = 1;
		[SerializeField] int _tileRows = 1;

		CountryVisualConfig _visualConfig;
		GS.Game.Configs.ProvinceConfig _provinceConfig;

		List<MapFeature> _provinceFeatures;

		[Inject]
		void Construct(CountryVisualConfig visualConfig, GS.Game.Configs.ProvinceConfig provinceConfig) {
			_visualConfig = visualConfig;
			_provinceConfig = provinceConfig;
		}

		public Map Load() {
			if (_provinceFeatures == null) {
				if (_provinceGeoJsonAsset == null) {
					Debug.LogError("MapLoader: province GeoJSON asset not assigned.");
					_provinceFeatures = new List<MapFeature>();
				} else {
					_provinceFeatures = GeoJsonParser.Parse(_provinceGeoJsonAsset.text);
					Debug.Log($"MapLoader: parsed {_provinceFeatures.Count} province features.");
				}
			}

			var map = Instantiate(_mapPrefab);
			map.Initialize(_provinceFeatures, _mapTiles, _tileCols, _tileRows, _visualConfig, _provinceConfig);
			return map;
		}
	}
}
