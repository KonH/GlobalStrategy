using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class Map : MonoBehaviour {
		[SerializeField] ProvinceRenderer _provinceRenderer;
		[SerializeField] MapImageOverlay _overlay;

		public ProvinceRenderer ProvinceRenderer => _provinceRenderer;

		public void Initialize(List<MapFeature> provinceFeatures, Texture2D[] tiles, int cols, int rows, CountryVisualConfig visualConfig, GS.Game.Configs.ProvinceConfig provinceConfig) {
			_provinceRenderer.Render(provinceFeatures, provinceConfig, visualConfig);
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
