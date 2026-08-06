using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Unity.Common;

namespace GS.Unity.Map {
	[RequireComponent(typeof(Camera))]
	public class MapClickHandler : MonoBehaviour {
		const float ClickDragThresholdPixels = 10f;

		Camera _camera;
		MapController _mapController;
		IWriteOnlyCommandAccessor _commands;
		VisualState _state;
		ModalState _modalState;
		UIPointerState _pointerState;
		bool _pressing;
		Vector2 _pressScreenPos;
		bool _pressIsTouch;
		int _pressTouchId;

		[Inject]
		void Construct(MapController mapController, IWriteOnlyCommandAccessor commands, VisualState state, ModalState modalState, UIPointerState pointerState) {
			_mapController = mapController;
			_commands = commands;
			_state = state;
			_modalState = modalState;
			_pointerState = pointerState;
		}

		void Awake() {
			_camera = GetComponent<Camera>();
		}

		void Update() {
			var mouse = Mouse.current;
			var touchscreen = Touchscreen.current;

			if (mouse != null && mouse.leftButton.wasPressedThisFrame) {
				BeginPress(mouse.position.ReadValue(), isTouch: false, pointerId: -1);
			} else if (touchscreen != null) {
				foreach (var touch in touchscreen.touches) {
					if (!touch.press.wasPressedThisFrame) { continue; }
					BeginPress(touch.position.ReadValue(), isTouch: true, pointerId: touch.touchId.ReadValue());
					break;
				}
			}

			if (!TryGetReleaseThisFrame(mouse, touchscreen, out Vector2 releasePos)) {
				return;
			}
			if (!_pressing) {
				return;
			}
			_pressing = false;
			if (_modalState.IsLocked()) {
				return;
			}

			if (Vector2.Distance(_pressScreenPos, releasePos) > ClickDragThresholdPixels) {
				return;
			}

			// Re-check at release, not just at press: a hand refresh (e.g. a cooldown card's
			// fraction ticking) can rebuild UI elements between press and release, leaving the
			// press-time Pick() briefly stale. Re-checking here closes that race instead of
			// letting the click fall through to the map underneath.
			if (_pointerState.IsPointerOverUI(releasePos)) {
				return;
			}

			Debug.Log($"[MapClick] Left click at screen {releasePos}");

			if (_state != null && _state.MapLens.Lens == MapLens.Province) {
				HandleProvinceClick(releasePos);
				return;
			}

			var provinceRenderer = _mapController != null ? _mapController.ActiveProvinceRenderer : null;
			if (provinceRenderer == null) {
				Debug.LogWarning($"[MapClick] provinceRenderer is null (mapController={_mapController != null})");
				return;
			}

			var world = _camera.ScreenToWorldPoint(new Vector3(releasePos.x, releasePos.y, 0f));
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

		void BeginPress(Vector2 screenPos, bool isTouch, int pointerId) {
			_pressScreenPos = screenPos;
			_pressIsTouch = isTouch;
			_pressTouchId = pointerId;
			_pressing = !_modalState.IsLocked() && !_pointerState.IsPointerOverUI(screenPos);
			if (!_pressing) {
				Debug.Log("[MapClick] Blocked by UI");
			}
		}

		bool TryGetReleaseThisFrame(Mouse mouse, Touchscreen touchscreen, out Vector2 releasePos) {
			if (!_pressIsTouch) {
				if (mouse != null && mouse.leftButton.wasReleasedThisFrame) {
					releasePos = mouse.position.ReadValue();
					return true;
				}
				releasePos = default;
				return false;
			}
			if (touchscreen != null) {
				foreach (var touch in touchscreen.touches) {
					if (touch.touchId.ReadValue() != _pressTouchId) { continue; }
					if (!touch.press.wasReleasedThisFrame) { continue; }
					releasePos = touch.position.ReadValue();
					return true;
				}
			}
			releasePos = default;
			return false;
		}

		void HandleProvinceClick(Vector2 screenPos) {
			var provinceRenderer = _mapController != null ? _mapController.ActiveProvinceRenderer : null;
			if (provinceRenderer == null) {
				Debug.LogWarning($"[MapClick] provinceRenderer is null (mapController={_mapController != null})");
				return;
			}

			var world = _camera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
			Debug.Log($"[MapClick] World pos: {world.x:F2}, {world.y:F2}");

			var id = provinceRenderer.FindFeatureAt(new Vector2(world.x, world.y));
			if (id == null) {
				Debug.Log("[MapClick] ocean (province lens)");
				_commands?.Push(new SelectProvinceCommand { ProvinceId = "" });
				return;
			}

			string provinceId = id.gameObject.name;
			Debug.Log($"[MapClick] provinceId: '{provinceId}', countryId: '{id.CountryId}'");
			_commands?.Push(new SelectProvinceCommand { ProvinceId = provinceId });
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
