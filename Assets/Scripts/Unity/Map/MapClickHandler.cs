using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using GS.Main;
using GS.Game.Commands;

namespace GS.Unity.Map {
	[RequireComponent(typeof(Camera))]
	public class MapClickHandler : MonoBehaviour {
		Camera _camera;
		MapController _mapController;
		CountryConfig _countryConfig;
		IWriteOnlyCommandAccessor _commands;

		[Inject]
		void Construct(MapController mapController, CountryConfig countryConfig, IWriteOnlyCommandAccessor commands) {
			_mapController = mapController;
			_countryConfig = countryConfig;
			_commands = commands;
		}

		void Awake() {
			_camera = GetComponent<Camera>();
		}

		void Update() {
			var mouse = Mouse.current;
			if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
			var mapRenderer = _mapController != null ? _mapController.ActiveRenderer : null;
			if (mapRenderer == null) return;

			var sp = mouse.position.ReadValue();
			var world = _camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));

			var id = mapRenderer.FindFeatureAt(new Vector2(world.x, world.y));
			if (id == null) {
				Debug.Log("[MapClick] ocean");
				_commands?.Push(new SelectCountryCommand(""));
				return;
			}

			string mapFeatureId = id.gameObject.name;
			var country = _countryConfig != null ? _countryConfig.FindByFeatureId(mapFeatureId) : null;

			if (country != null) {
				bool isMain = country.mainMapFeatureIds.Contains(mapFeatureId);
				string role = isMain ? "main" : "secondary";
				Debug.Log($"[MapClick] {country.displayName} (id: {country.countryId}, role: {role})");
				_commands?.Push(new SelectCountryCommand(country.countryId));
			} else {
				Debug.Log($"[MapClick] {id.FeatureName} (id: {mapFeatureId})");
				_commands?.Push(new SelectCountryCommand(mapFeatureId));
			}
		}
	}
}
