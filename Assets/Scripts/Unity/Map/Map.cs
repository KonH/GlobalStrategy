using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class Map : MonoBehaviour {
		[SerializeField] MapRenderer _renderer;
		[SerializeField] MapImageOverlay _overlay;

		public MapRenderer Renderer => _renderer;

		public void Initialize(List<MapFeature> features, Texture2D[] tiles, int cols, int rows, CountryConfig countryConfig, CountryVisualConfig visualConfig) {
			_renderer.Render(features, countryConfig, visualConfig);
			if (tiles != null && tiles.Length > 0) {
				if (cols == 1 && rows == 1) {
					_overlay.Setup(tiles[0]);
				} else {
					_overlay.Setup(tiles, cols, rows);
				}
			}
		}
	}
}
