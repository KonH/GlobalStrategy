using System.Collections.Generic;
using UnityEngine;
using VContainer;
using GS.Core.Map;
using DomainCountryConfig = GS.Game.Configs.CountryConfig;

namespace GS.Unity.Map {
	public class MapLoader : MonoBehaviour {
		[SerializeField] Map _mapPrefab;
		[SerializeField] TextAsset _geoJsonAsset;
		[SerializeField] Texture2D[] _mapTiles;
		[SerializeField] int _tileCols = 1;
		[SerializeField] int _tileRows = 1;

		CountryVisualConfig _visualConfig;
		DomainCountryConfig _domainCountryConfig;

		List<MapFeature> _features;

		[Inject]
		void Construct(CountryVisualConfig visualConfig, DomainCountryConfig domainCountryConfig) {
			_visualConfig = visualConfig;
			_domainCountryConfig = domainCountryConfig;
		}

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
			map.Initialize(_features, _mapTiles, _tileCols, _tileRows, _visualConfig, _domainCountryConfig);
			return map;
		}
	}
}
