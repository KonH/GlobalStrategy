using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;
using DomainCountryConfig = GS.Game.Configs.CountryConfig;

namespace GS.Unity.Map {
	public class Map : MonoBehaviour {
		[SerializeField] MapRenderer _renderer;
		[SerializeField] MapImageOverlay _overlay;

		public MapRenderer Renderer => _renderer;

		public void Initialize(List<MapFeature> features, Texture2D[] tiles, int cols, int rows, CountryVisualConfig visualConfig, DomainCountryConfig domainConfig) {
			_renderer.Render(features, visualConfig, domainConfig);
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
