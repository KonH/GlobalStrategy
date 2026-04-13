using System.IO;
using UnityEngine;
using UnityEditor;
using GS.Unity.Map;

namespace GS.Editor.Map {
	public static class MapTextureSplitter {
		const string DefaultSourcePath = "../MapSource/NE1_LR_LC_SR_W_DR.jpg";
		const string OutputFolder = "Assets/Map/Tiles";
		const int Cols = 8;
		const int Rows = 4;

		[MenuItem("Game/Map/Split Map Texture")]
		static void SplitMapTexture() {
			string defaultPath = Path.GetFullPath(DefaultSourcePath);
			string sourcePath = EditorUtility.OpenFilePanelWithFilters(
				"Select Source Map Texture",
				File.Exists(defaultPath) ? Path.GetDirectoryName(defaultPath) : Application.dataPath,
				new[] { "Image files", "jpg,jpeg,png", "All files", "*" });

			if (string.IsNullOrEmpty(sourcePath)) {
				if (File.Exists(defaultPath)) {
					sourcePath = defaultPath;
				} else {
					Debug.LogError($"[MapTextureSplitter] No source file selected and default not found at: {defaultPath}");
					return;
				}
			}

			byte[] imageBytes = File.ReadAllBytes(sourcePath);
			var sourceTexture = new Texture2D(2, 2);
			if (!ImageConversion.LoadImage(sourceTexture, imageBytes)) {
				Debug.LogError($"[MapTextureSplitter] Failed to load image: {sourcePath}");
				Object.DestroyImmediate(sourceTexture);
				return;
			}

			int srcW = sourceTexture.width;
			int srcH = sourceTexture.height;
			int tileW = srcW / Cols;
			int tileH = srcH / Rows;

			if (!Directory.Exists(OutputFolder)) {
				Directory.CreateDirectory(OutputFolder);
			}

			for (int r = 0; r < Rows; r++) {
				for (int c = 0; c < Cols; c++) {
					// Unity texture origin is bottom-left; image row 0 is at the top (srcH - tileH)
					int pixelX = c * tileW;
					int pixelY = srcH - (r + 1) * tileH;

					var tile = new Texture2D(tileW, tileH, TextureFormat.RGB24, false);
					tile.SetPixels(sourceTexture.GetPixels(pixelX, pixelY, tileW, tileH));
					tile.Apply();

					string tilePath = $"{OutputFolder}/tile_r{r}_c{c}.png";
					File.WriteAllBytes(tilePath, ImageConversion.EncodeToPNG(tile));
					Object.DestroyImmediate(tile);
				}
			}

			Object.DestroyImmediate(sourceTexture);

			AssetDatabase.Refresh();

			for (int r = 0; r < Rows; r++) {
				for (int c = 0; c < Cols; c++) {
					string tilePath = $"{OutputFolder}/tile_r{r}_c{c}.png";
					var importer = (TextureImporter)AssetImporter.GetAtPath(tilePath);
					if (importer == null) {
						continue;
					}
					importer.textureType = TextureImporterType.Default;
					importer.isReadable = false;
					importer.maxTextureSize = 2048;
					importer.mipmapEnabled = false;
					importer.filterMode = FilterMode.Bilinear;
					importer.npotScale = TextureImporterNPOTScale.None;
					importer.SaveAndReimport();
				}
			}

			AssignTilesToMapLoader();

			Debug.Log($"[MapTextureSplitter] Done. Generated {Rows * Cols} tiles in {OutputFolder}.");
		}

		static void AssignTilesToMapLoader() {
			var mapLoader = Object.FindAnyObjectByType<MapLoader>();
			if (mapLoader == null) {
				Debug.LogWarning("[MapTextureSplitter] MapLoader not found in scene. Assign tiles manually in the Inspector.");
				return;
			}

			var so = new SerializedObject(mapLoader);
			var tilesProp = so.FindProperty("_mapTiles");
			var colsProp = so.FindProperty("_tileCols");
			var rowsProp = so.FindProperty("_tileRows");

			if (tilesProp == null || colsProp == null || rowsProp == null) {
				Debug.LogWarning("[MapTextureSplitter] Could not find serialized properties on MapLoader.");
				return;
			}

			tilesProp.arraySize = Rows * Cols;
			for (int r = 0; r < Rows; r++) {
				for (int c = 0; c < Cols; c++) {
					string tilePath = $"{OutputFolder}/tile_r{r}_c{c}.png";
					var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tilePath);
					tilesProp.GetArrayElementAtIndex(r * Cols + c).objectReferenceValue = tex;
				}
			}
			colsProp.intValue = Cols;
			rowsProp.intValue = Rows;
			so.ApplyModifiedProperties();
			EditorUtility.SetDirty(mapLoader);

			Debug.Log("[MapTextureSplitter] Tiles assigned to MapLoader.");
		}
	}
}
