using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class Map : MonoBehaviour {
		[SerializeField] MapRenderer _renderer;
		[SerializeField] MapImageOverlay _overlay;

		public MapRenderer Renderer => _renderer;

		public void Initialize(List<MapFeature> features, Texture2D texture, MapFeatureConfig mapFeatureConfig, CountryConfig countryConfig) {
			_renderer.Render(features, mapFeatureConfig, countryConfig);
			if (texture != null)
				_overlay.Setup(texture);
		}
	}
}
