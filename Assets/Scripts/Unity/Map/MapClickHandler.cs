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
		IWriteOnlyCommandAccessor _commands;
		VisualState _state;

		[Inject]
		void Construct(MapController mapController, IWriteOnlyCommandAccessor commands, VisualState state) {
			_mapController = mapController;
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
				Debug.Log("[MapClick] ocean");
				_commands?.Push(new SelectCountryCommand(""));
				return;
			}

			string ownerId = ResolveOwner(id);
			Debug.Log($"[MapClick] provinceId: '{id.gameObject.name}', ownerId: '{ownerId}'");
			_commands?.Push(new SelectCountryCommand(ownerId));
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
			Debug.Log($"[MapClick] provinceId: '{provinceId}', countryId: '{id.CountryId}'");
			_commands.Push(new SelectProvinceCommand { ProvinceId = provinceId });
		}

		string ResolveOwner(ProvinceIdentifier id) {
			var owners = _state?.ProvinceOwnership?.OwnerByProvinceId;
			if (owners != null && owners.TryGetValue(id.ProvinceId, out string ownerId)) {
				return ownerId;
			}
			return id.CountryId;
		}
	}
}
