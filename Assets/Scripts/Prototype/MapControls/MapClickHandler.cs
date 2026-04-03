using UnityEngine;
using UnityEngine.InputSystem;
using GS.Unity.Map;

namespace GS.Prototype.MapControls {
	[RequireComponent(typeof(Camera))]
	public class MapClickHandler : MonoBehaviour {
		Camera _camera;
		MapRenderer _mapRenderer;

		void Awake() {
			_camera = GetComponent<Camera>();
			_mapRenderer = FindObjectOfType<MapRenderer>();
		}

		void Update() {
			var mouse = Mouse.current;
			if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
			if (_mapRenderer == null) return;

			var sp = mouse.position.ReadValue();
			// For orthographic camera XY world pos is screen-pos mapped regardless of Z
			var world = _camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));

			var id = _mapRenderer.FindFeatureAt(new Vector2(world.x, world.y));
			if (id != null)
				Debug.Log($"[MapClick] {id.FeatureName} (id: {id.FeatureId})");
			else
				Debug.Log("[MapClick] ocean");
		}
	}
}
