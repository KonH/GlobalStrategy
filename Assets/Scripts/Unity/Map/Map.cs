using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class Map : MonoBehaviour {
		[SerializeField] MapRenderer _renderer;
		[SerializeField] MapImageOverlay _overlay;

		public void Initialize(List<MapFeature> features, Texture2D texture) {
			_renderer.Render(features);
			if (texture != null)
				_overlay.Setup(texture);
		}
	}
}
