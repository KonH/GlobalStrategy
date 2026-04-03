using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class MapLoader : MonoBehaviour {
		[SerializeField] TextAsset _geoJsonAsset;
		[SerializeField] Texture2D _mapTexture;
		[SerializeField] MapRenderer _mapRenderer;
		[SerializeField] MapImageOverlay _imageOverlay;

		void Start() {
			if (_geoJsonAsset == null) {
				Debug.LogError("MapLoader: GeoJSON asset not assigned.");
				return;
			}

			var features = GeoJsonParser.Parse(_geoJsonAsset.text);
			Debug.Log($"MapLoader: parsed {features.Count} features.");
			_mapRenderer.Render(features);

			if (_mapTexture != null && _imageOverlay != null)
				_imageOverlay.Setup(_mapTexture);
		}
	}
}
