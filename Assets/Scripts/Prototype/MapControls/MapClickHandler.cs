using UnityEngine;
using UnityEngine.InputSystem;
using GS.Unity.Map;

namespace GS.Prototype.MapControls {
	[RequireComponent(typeof(Camera))]
	public class MapClickHandler : MonoBehaviour {
		Camera _camera;
		[SerializeField] MapController _mapController;
		[SerializeField] CountryConfig _countryConfig;

		void Awake() {
			_camera = GetComponent<Camera>();
		}

		void Update() {
			var mouse = Mouse.current;
			if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
			var _mapRenderer = _mapController != null ? _mapController.ActiveRenderer : null;
			if (_mapRenderer == null) return;

			var sp = mouse.position.ReadValue();
			// For orthographic camera XY world pos is screen-pos mapped regardless of Z
			var world = _camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));

			var id = _mapRenderer.FindFeatureAt(new Vector2(world.x, world.y));
			if (id == null) {
				Debug.Log("[MapClick] ocean");
				return;
			}

			string mapFeatureId = id.gameObject.name;
			if (_countryConfig != null) {
				var country = _countryConfig.FindByFeatureId(mapFeatureId);
				if (country != null) {
					bool isMain = country.mainMapFeatureIds.Contains(mapFeatureId);
					string role = isMain ? "main" : "secondary";
					Debug.Log($"[MapClick] {country.displayName} (id: {country.countryId}, role: {role})");
					return;
				}
			}

			Debug.Log($"[MapClick] {id.FeatureName} (id: {mapFeatureId})");
		}
	}
}
