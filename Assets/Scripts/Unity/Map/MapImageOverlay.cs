using UnityEngine;

namespace GS.Unity.Map {
	[RequireComponent(typeof(Renderer))]
	public class MapImageOverlay : MonoBehaviour {
		public void Setup(Texture2D texture) {
			var mat = new Material(Shader.Find("Unlit/Texture"));
			mat.mainTexture = texture;
			GetComponent<Renderer>().material = mat;
			transform.localScale = new Vector3(CoordinateConverter.MapWidth, CoordinateConverter.MapHeight, 1f);
		}
	}
}
