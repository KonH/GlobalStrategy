using UnityEngine;

namespace GS.Unity.Map {
	[CreateAssetMenu(fileName = "MapCameraConfig", menuName = "GlobalStrategy/Map Camera Config")]
	public class MapCameraConfig : ScriptableObject {
		public float PanSpeed = 80f;
		public float ZoomSpeed = 10f;
		public float MinZoom = 20f;
		public float MaxZoom = 200f;
	}
}
