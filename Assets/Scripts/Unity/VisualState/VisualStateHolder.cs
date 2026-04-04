using UnityEngine;

namespace GS.Unity.VisualState {
	public class VisualStateHolder : MonoBehaviour {
		public VisualState State { get; } = new VisualState();
	}
}
