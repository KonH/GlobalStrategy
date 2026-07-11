using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Unity.Common;

namespace GS.Unity.Map {
	[RequireComponent(typeof(Camera))]
	public class MapClickHandler : MonoBehaviour {
		Camera _camera;
		MapController _mapController;
		GS.Game.Configs.CountryConfig _domainCountryConfig;
		IWriteOnlyCommandAccessor _commands;
		VisualState _state;

		[Inject]
		void Construct(MapController mapController, GS.Game.Configs.CountryConfig domainCountryConfig, IWriteOnlyCommandAccessor commands, VisualState state) {
			_mapController = mapController;
			_domainCountryConfig = domainCountryConfig;
			_commands = commands;
			_state = state;
		}

		void Awake() {
			_camera = GetComponent<Camera>();
		}

		void Update() {
			var mouse = Mouse.current;
			if (mouse == null) {
				return;
			}
			if (!mouse.leftButton.wasPressedThisFrame) {
				return;
			}
			if (ModalState.IsModalOpen) {
				return;
			}
			Debug.Log($"[MapClick] Left click at screen {mouse.position.ReadValue()}");
			if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) {
				Debug.Log("[MapClick] Blocked by UI (IsPointerOverGameObject)");
				return;
			}

			if (_state != null && _state.MapLens.Lens == MapLens.Province) {
				HandleProvinceClick(mouse);
				return;
			}

			var mapRenderer = _mapController != null ? _mapController.ActiveRenderer : null;
			if (mapRenderer == null) {
				Debug.LogWarning($"[MapClick] mapRenderer is null (mapController={_mapController != null})");
				return;
			}

			var sp = mouse.position.ReadValue();
			var world = _camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));
			Debug.Log($"[MapClick] World pos: {world.x:F2}, {world.y:F2}");

			var id = mapRenderer.FindFeatureAt(new Vector2(world.x, world.y));
			if (id == null) {
				Debug.Log("[MapClick] ocean");
				_commands?.Push(new SelectCountryCommand(""));
				return;
			}

			string mapFeatureId = id.gameObject.name;
			var country = _domainCountryConfig?.FindByFeatureId(mapFeatureId);

			if (country != null) {
				bool isMain = country.MainMapFeatureIds.Contains(mapFeatureId);
				string role = isMain ? "main" : "secondary";
				Debug.Log($"[MapClick] countryId: '{country.CountryId}', featureId: '{mapFeatureId}', role: {role}");
				_commands?.Push(new SelectCountryCommand(country.CountryId));
			} else {
				Debug.Log($"[MapClick] No country entry for featureId: '{mapFeatureId}' — pushing raw id");
				_commands?.Push(new SelectCountryCommand(mapFeatureId));
			}
		}

		void HandleProvinceClick(Mouse mouse) {
			var provinceRenderer = _mapController != null ? _mapController.ActiveProvinceRenderer : null;
			if (provinceRenderer == null) {
				Debug.LogWarning($"[MapClick] provinceRenderer is null (mapController={_mapController != null})");
				return;
			}

			var sp = mouse.position.ReadValue();
			var world = _camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));
			Debug.Log($"[MapClick] World pos: {world.x:F2}, {world.y:F2}");

			var id = provinceRenderer.FindFeatureAt(new Vector2(world.x, world.y));
			if (id == null) {
				Debug.Log("[MapClick] ocean (province lens)");
				return;
			}

			string provinceId = id.gameObject.name;
			// TODO: hook up province selection UI here
			Debug.Log($"[MapClick] provinceId: '{provinceId}', countryId: '{id.CountryId}'");
		}
	}
}
