using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace GS.Editor.Localization {
	/// <summary>
	/// Restores every UI font asset to its empty dynamic-atlas baseline after Play mode.
	/// </summary>
	[InitializeOnLoad]
	public static class DynamicFontAtlasCleanup {
		const string FontAssetsFolder = "Assets/UI/Fonts";

		static DynamicFontAtlasCleanup() {
			EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
		}

		[MenuItem("GS/Localization/Clear Dynamic Font Data")]
		static void ClearDynamicFontData() {
			int cleared = 0;
			try {
				AssetDatabase.StartAssetEditing();
				foreach (var font in FindFontAssets()) {
					if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic) {
						throw new InvalidOperationException($"[DynamicFontAtlasCleanup] '{font.name}' must use Dynamic atlas population.");
					}

					EnsureDynamicDataIsClearedOnBuild(font);
					font.ClearFontAssetData();
					EditorUtility.SetDirty(font);
					cleared++;
				}
			} finally {
				AssetDatabase.StopAssetEditing();
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[DynamicFontAtlasCleanup] Restored {cleared} dynamic font assets to their commit baseline.");
		}

		static void HandlePlayModeStateChanged(PlayModeStateChange state) {
			if (state != PlayModeStateChange.EnteredEditMode) {
				return;
			}

			ClearDynamicFontData();
		}

		static IReadOnlyList<FontAsset> FindFontAssets() {
			string[] guids = AssetDatabase.FindAssets("t:FontAsset", new[] { FontAssetsFolder });
			var fonts = new List<FontAsset>(guids.Length);
			foreach (string guid in guids) {
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var font = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
				if (font == null) {
					throw new InvalidOperationException($"[DynamicFontAtlasCleanup] '{path}' is not a TextCore FontAsset.");
				}

				fonts.Add(font);
			}

			if (fonts.Count == 0) {
				throw new InvalidOperationException($"[DynamicFontAtlasCleanup] No FontAsset files found in {FontAssetsFolder}.");
			}

			return fonts;
		}

		static void EnsureDynamicDataIsClearedOnBuild(FontAsset font) {
			var serializedFont = new SerializedObject(font);
			var clearOnBuild = serializedFont.FindProperty("m_ClearDynamicDataOnBuild");
			if (clearOnBuild == null) {
				throw new InvalidOperationException($"[DynamicFontAtlasCleanup] '{font.name}' has no Clear Dynamic Data On Build setting.");
			}

			if (!clearOnBuild.boolValue) {
				clearOnBuild.boolValue = true;
				serializedFont.ApplyModifiedPropertiesWithoutUndo();
			}
		}
	}
}
