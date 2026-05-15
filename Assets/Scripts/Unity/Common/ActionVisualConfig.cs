using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Common {
	[System.Serializable]
	public class ActionVisualEntry {
		public string actionId;
		public Sprite frontImage;
		public Sprite backImage;
	}

	[CreateAssetMenu(fileName = "ActionVisualConfig", menuName = "GS/ActionVisualConfig")]
	public class ActionVisualConfig : ScriptableObject {
		public Sprite defaultBackImage;
		public List<ActionVisualEntry> entries = new();

		public Sprite FindFront(string actionId) {
			foreach (var e in entries) {
				if (e.actionId == actionId) { return e.frontImage; }
			}
			return null;
		}

		public Sprite FindBack(string actionId) {
			foreach (var e in entries) {
				if (e.actionId == actionId && e.backImage != null) { return e.backImage; }
			}
			return defaultBackImage;
		}
	}
}
