using System.IO;
using UnityEditor;
using UnityEngine;
using GS.Unity.Common;

namespace GS.Editor.Characters {
	public static class CharacterVisualConfigPopulator {
		const string ConfigPath = "Assets/Configs/CharacterVisualConfig.asset";
		const string TexturesFolder = "Assets/Textures/Characters";

		[MenuItem("GS/Tools/Populate Character Visual Config")]
		static void Populate() {
			var config = AssetDatabase.LoadAssetAtPath<CharacterVisualConfig>(ConfigPath);
			if (config == null) {
				Debug.LogError($"[CharacterVisualConfigPopulator] Asset not found at {ConfigPath}");
				return;
			}

			config.entries.Clear();
			var guids = AssetDatabase.FindAssets("t:Sprite", new[] { TexturesFolder });
			foreach (var guid in guids) {
				var path = AssetDatabase.GUIDToAssetPath(guid);
				var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
				if (sprite == null) {
					continue;
				}
				var characterId = Path.GetFileNameWithoutExtension(path);
				config.entries.Add(new CharacterVisualEntry { characterId = characterId, portrait = sprite });
			}

			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			if (config.entries.Count == 0) {
				Debug.LogWarning($"[CharacterVisualConfigPopulator] No sprites found in {TexturesFolder}. Ensure textures are imported as Sprite.");
			}
			Debug.Log($"[CharacterVisualConfigPopulator] Populated {config.entries.Count} entries.");
		}
	}
}
