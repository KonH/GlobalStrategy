using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Common {
	[CreateAssetMenu(fileName = "CharacterVisualConfig", menuName = "GS/CharacterVisualConfig")]
	public class CharacterVisualConfig : ScriptableObject {
		public List<CharacterVisualEntry> entries = new();

		public Sprite FindPortrait(string characterId) {
			foreach (var e in entries) {
				if (e.characterId == characterId) {
					return e.portrait;
				}
			}
			return null;
		}
	}
}
