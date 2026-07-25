using System.IO;
using UnityEditor;
using UnityEngine;

namespace GS.Editor.Textures {
	/// <summary>
	/// Applies Sprite import settings matching
	/// Assets/Textures/Flags/Countries/Argentina.png.meta
	/// to all textures under Assets/Textures/Flags and Assets/Textures/Icons.
	/// </summary>
	public sealed class UiTexturePostprocessor : AssetPostprocessor {
		const string TemplatePath = "Assets/Textures/Flags/Countries/Argentina.png";

		static readonly string[] TargetFolders = {
			"Assets/Textures/Flags",
			"Assets/Textures/Icons",
		};

		static readonly string[] PlatformNames = {
			"Standalone",
			"WebGL",
		};

		static TextureImporterSettings _settings;
		static TextureImporterPlatformSettings _defaultPlatform;
		static TextureImporterPlatformSettings[] _platforms;
		static bool _cacheReady;

		[InitializeOnLoadMethod]
		static void WarmCache() {
			TryRefreshCacheFromTemplate();
		}

		void OnPreprocessTexture() {
			if (!IsTargetPath(assetPath)) {
				return;
			}

			// Never re-enter while the template itself is importing.
			if (assetPath.Replace('\\', '/') == TemplatePath) {
				return;
			}

			if (!EnsureCache(out string error)) {
				Debug.LogError($"[UiTexturePostprocessor] {assetPath}: {error}");
				return;
			}

			ApplyCachedSettings((TextureImporter)assetImporter);
		}

		[MenuItem("GS/Textures/Apply UI Texture Import Settings")]
		static void ApplyToExisting() {
			if (!File.Exists(TemplatePath)) {
				Debug.LogError($"[UiTexturePostprocessor] Template missing: {TemplatePath}");
				return;
			}

			TryRefreshCacheFromTemplate();
			if (!EnsureCache(out string error)) {
				Debug.LogError($"[UiTexturePostprocessor] {error}");
				return;
			}

			string[] guids = AssetDatabase.FindAssets("t:Texture2D", TargetFolders);
			int updated = 0;
			try {
				AssetDatabase.StartAssetEditing();
				for (int i = 0; i < guids.Length; i++) {
					string path = AssetDatabase.GUIDToAssetPath(guids[i]);
					if (!IsTargetPath(path) || path.Replace('\\', '/') == TemplatePath) {
						continue;
					}

					EditorUtility.DisplayProgressBar(
						"UI Texture Import Settings",
						path,
						guids.Length == 0 ? 1f : (float)i / guids.Length);

					var importer = AssetImporter.GetAtPath(path) as TextureImporter;
					if (importer == null) {
						continue;
					}

					ApplyCachedSettings(importer);
					importer.SaveAndReimport();
					updated++;
				}
			} finally {
				AssetDatabase.StopAssetEditing();
				EditorUtility.ClearProgressBar();
			}

			Debug.Log($"[UiTexturePostprocessor] Applied Argentina import settings to {updated} texture(s).");
		}

		static bool IsTargetPath(string path) {
			if (string.IsNullOrEmpty(path)) {
				return false;
			}

			string normalized = path.Replace('\\', '/');
			for (int i = 0; i < TargetFolders.Length; i++) {
				string folder = TargetFolders[i];
				if (normalized.StartsWith(folder + "/") || normalized == folder) {
					return true;
				}
			}

			return false;
		}

		static bool EnsureCache(out string error) {
			if (_cacheReady) {
				error = null;
				return true;
			}

			if (TryRefreshCacheFromTemplate()) {
				error = null;
				return true;
			}

			// AssetPostprocessor often cannot load other importers mid-import.
			// Fall back to the settings encoded in Argentina.png.meta.
			ApplyArgentinaMetaDefaultsToCache();
			_cacheReady = true;
			error = null;
			return true;
		}

		static bool TryRefreshCacheFromTemplate() {
			var template = AssetImporter.GetAtPath(TemplatePath) as TextureImporter;
			if (template == null) {
				return false;
			}

			_settings = new TextureImporterSettings();
			template.ReadTextureSettings(_settings);

			_defaultPlatform = new TextureImporterPlatformSettings();
			template.GetDefaultPlatformTextureSettings().CopyTo(_defaultPlatform);

			_platforms = new TextureImporterPlatformSettings[PlatformNames.Length];
			for (int i = 0; i < PlatformNames.Length; i++) {
				_platforms[i] = new TextureImporterPlatformSettings();
				template.GetPlatformTextureSettings(PlatformNames[i]).CopyTo(_platforms[i]);
			}

			_cacheReady = true;
			return true;
		}

		static void ApplyCachedSettings(TextureImporter target) {
			target.SetTextureSettings(_settings);
			target.SetPlatformTextureSettings(_defaultPlatform);
			for (int i = 0; i < _platforms.Length; i++) {
				target.SetPlatformTextureSettings(_platforms[i]);
			}
		}

		/// <summary>
		/// Mirrors Assets/Textures/Flags/Countries/Argentina.png.meta when the live
		/// template importer cannot be loaded (typical during OnPreprocessTexture).
		/// </summary>
		static void ApplyArgentinaMetaDefaultsToCache() {
			_settings = new TextureImporterSettings();
			_settings.textureType = TextureImporterType.Sprite;
			_settings.spriteMode = (int)SpriteImportMode.Single;
			_settings.spritePixelsPerUnit = 100f;
			_settings.spriteAlignment = (int)SpriteAlignment.Center;
			_settings.spritePivot = new Vector2(0.5f, 0.5f);
			_settings.spriteBorder = Vector4.zero;
			_settings.spriteExtrude = 1;
			_settings.spriteMeshType = SpriteMeshType.FullRect;
			_settings.spriteGenerateFallbackPhysicsShape = true;
			_settings.mipmapEnabled = false;
			_settings.sRGBTexture = true;
			_settings.readable = false;
			_settings.npotScale = TextureImporterNPOTScale.None;
			_settings.filterMode = FilterMode.Bilinear;
			_settings.aniso = 1;
			_settings.wrapMode = TextureWrapMode.Clamp;
			_settings.wrapModeU = TextureWrapMode.Clamp;
			_settings.wrapModeV = TextureWrapMode.Clamp;
			_settings.wrapModeW = TextureWrapMode.Clamp;
			_settings.alphaSource = TextureImporterAlphaSource.FromInput;
			_settings.alphaIsTransparency = true;
			_settings.fadeOut = false;
			_settings.borderMipmap = false;
			_settings.mipMapsPreserveCoverage = false;
			_settings.alphaTestReferenceValue = 0.5f;
			_settings.convertToNormalMap = false;
			_settings.heightmapScale = 0.25f;
			_settings.normalMapFilter = TextureImporterNormalFilter.Standard;
			_settings.textureShape = TextureImporterShape.Texture2D;

			_defaultPlatform = new TextureImporterPlatformSettings {
				name = "DefaultTexturePlatform",
				overridden = false,
				maxTextureSize = 2048,
				resizeAlgorithm = TextureResizeAlgorithm.Mitchell,
				format = TextureImporterFormat.Automatic,
				textureCompression = TextureImporterCompression.Uncompressed,
				compressionQuality = 50,
				crunchedCompression = false,
				allowsAlphaSplitting = false,
			};

			_platforms = new[] {
				new TextureImporterPlatformSettings {
					name = "Standalone",
					overridden = false,
					maxTextureSize = 2048,
					resizeAlgorithm = TextureResizeAlgorithm.Bilinear,
					format = TextureImporterFormat.Automatic,
					textureCompression = TextureImporterCompression.Compressed,
					compressionQuality = 50,
					crunchedCompression = false,
					allowsAlphaSplitting = false,
				},
				new TextureImporterPlatformSettings {
					name = "WebGL",
					overridden = false,
					maxTextureSize = 2048,
					resizeAlgorithm = TextureResizeAlgorithm.Bilinear,
					format = TextureImporterFormat.Automatic,
					textureCompression = TextureImporterCompression.Compressed,
					compressionQuality = 50,
					crunchedCompression = false,
					allowsAlphaSplitting = false,
				},
			};
		}
	}
}
