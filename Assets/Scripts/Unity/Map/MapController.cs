using UnityEngine;
using VContainer;

namespace GS.Unity.Map {
	public class MapController : MonoBehaviour {
		MapLoader _loader;
		Camera _camera;

		Map _current;
		Map _forward;

		public MapRenderer ActiveRenderer => _current != null ? _current.Renderer : null;

		[Inject]
		void Construct(MapLoader loader, Camera camera) {
			_loader = loader;
			_camera = camera;
		}

		void Start() {
			_current = _loader.Load();
			_current.transform.position = new Vector3(0f, 0f, 0f);

			_forward = _loader.Load();
			_forward.transform.position = new Vector3(CoordinateConverter.MapWidth, 0f, 0f);
		}

		void Update() {
			PlaceForward();
			CheckSwap();
		}

		void PlaceForward() {
			float targetX = _camera.transform.position.x >= 0f
				? CoordinateConverter.MapWidth
				: -CoordinateConverter.MapWidth;
			var pos = _forward.transform.position;
			pos.x = targetX;
			_forward.transform.position = pos;
		}

		void CheckSwap() {
			float camX = _camera.transform.position.x;
			float half = CoordinateConverter.MapWidth / 2f;
			if (Mathf.Abs(camX) <= half) return;
			float shift = camX > 0f ? -CoordinateConverter.MapWidth : CoordinateConverter.MapWidth;
			var camPos = _camera.transform.position;
			camPos.x += shift;
			_camera.transform.position = camPos;
			(_current, _forward) = (_forward, _current);
			PlaceForward();
		}
	}
}
