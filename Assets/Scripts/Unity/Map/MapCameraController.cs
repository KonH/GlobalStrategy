using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace GS.Unity.Map {
	[RequireComponent(typeof(Camera))]
	public class MapCameraController : MonoBehaviour {
		Camera _camera;
		MapCameraConfig _config;
		bool _dragging;
		Vector3 _dragOriginWorld;

		[Inject]
		void Construct(MapCameraConfig config) {
			_config = config;
		}

		void Awake() {
			_camera = GetComponent<Camera>();
		}

		void Update() {
			HandleKeyboard();
			HandleZoom();
			UpdateDragState();
			WrapX();
			ClampY();
		}

		void HandleZoom() {
			var mouse = Mouse.current;
			if (mouse == null) return;
			float scroll = mouse.scroll.ReadValue().y;
			if (scroll == 0f) return;
			_camera.orthographicSize = Mathf.Clamp(
				_camera.orthographicSize - scroll * _config.ZoomSpeed,
				_config.MinZoom,
				_config.MaxZoom);
		}

		void HandleKeyboard() {
			var kb = Keyboard.current;
			if (kb == null) return;
			float h = 0f, v = 0f;
			if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
			if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
			if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
			if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
			transform.Translate(h * _config.PanSpeed * Time.deltaTime, v * _config.PanSpeed * Time.deltaTime, 0f, Space.World);
		}

		void UpdateDragState() {
			var mouse = Mouse.current;
			if (mouse == null) return;
			if (mouse.rightButton.wasPressedThisFrame) {
				var sp = mouse.position.ReadValue();
				_dragOriginWorld = _camera.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0f));
				_dragging = true;
			}
			if (mouse.rightButton.wasReleasedThisFrame) {
				_dragging = false;
			}
			if (!_dragging) return;
			var cur = mouse.position.ReadValue();
			Vector3 current = _camera.ScreenToWorldPoint(new Vector3(cur.x, cur.y, 0f));
			Vector3 delta = _dragOriginWorld - current;
			transform.position += new Vector3(delta.x, delta.y, 0f);
		}

		void ClampY() {
			float halfMap = CoordinateConverter.MapHeight / 2f;
			float margin = _camera.orthographicSize;
			float minY = -halfMap + margin;
			float maxY = halfMap - margin;
			Vector3 pos = transform.position;
			pos.y = Mathf.Clamp(pos.y, Mathf.Min(minY, maxY), Mathf.Max(minY, maxY));
			transform.position = pos;
		}

		void WrapX() {
			float half = CoordinateConverter.MapWidth / 2f;
			Vector3 pos = transform.position;
			if (pos.x > half) pos.x -= CoordinateConverter.MapWidth;
			else if (pos.x < -half) pos.x += CoordinateConverter.MapWidth;
			transform.position = pos;
		}
	}
}
