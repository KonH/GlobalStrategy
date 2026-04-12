using UnityEngine;

namespace GS.Unity.Map {
	public class MapScrollBackground : MonoBehaviour {
		[SerializeField] float _speed = 2f;

		void Update() {
			transform.position += Vector3.right * (_speed * Time.deltaTime);
		}
	}
}
