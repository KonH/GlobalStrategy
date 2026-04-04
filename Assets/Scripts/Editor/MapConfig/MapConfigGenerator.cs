using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using GS.Core.Map;
using GS.Unity.Map;

namespace GS.Editor.MapConfig {
	public static class MapConfigGenerator {
		const string GeoJsonPath = "Assets/Map/world_1880.json";
		const string FeatureConfigPath = "Assets/Configs/MapFeatureConfig.asset";
		const string CountryConfigPath = "Assets/Configs/CountryConfig.asset";

		static readonly Dictionary<string, Color> _predefinedColors = new Dictionary<string, Color> {
			{ "United_Kingdom_of_Great_Britain_and_Ireland", HexColor(0xC8, 0x78, 0x8C) },
			{ "France",                   HexColor(0x8C, 0xB4, 0xDC) },
			{ "Russian_Empire",           HexColor(0xA0, 0xC8, 0x7A) },
			{ "Germany",                  HexColor(0xC8, 0xB4, 0x78) },
			{ "Ottoman_Empire",           HexColor(0x78, 0xC0, 0xA0) },
			{ "Austria_Hungary",          HexColor(0xE8, 0xD8, 0x78) },
			{ "United_States_of_America", HexColor(0x78, 0xA0, 0xC8) },
			{ "Manchu_Empire",            HexColor(0xC8, 0xA0, 0x78) },
			{ "Spain",                    HexColor(0xC8, 0x96, 0x78) },
			{ "Portugal",                 HexColor(0x78, 0xC8, 0x78) },
			{ "Imperial_Japan",           HexColor(0xC8, 0x78, 0xA0) },
			{ "Italy",                    HexColor(0x90, 0xC8, 0x90) },
			{ "Netherlands",              HexColor(0xE8, 0xA0, 0x50) },
			{ "Kingdom_of_Brazil",        HexColor(0x78, 0xC8, 0xA0) },
			{ "SwedenNorway",             HexColor(0x78, 0x90, 0xC8) },
		};

		// Territory countryId → parent countryId.
		// Territory features are merged into the parent's secondaryMapFeatureIds;
		// the territory CountryEntry is removed so FindByFeatureId returns the parent.
		static readonly Dictionary<string, string> _colonialParents = new Dictionary<string, string> {
			{ "Gold_Coast_GB",                "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Hong_Kong",                    "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "New_South_Wales_UK",           "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Northern_Territory_UK",        "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Queensland_UK",                "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "South_Australia_UK",           "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Western_Australia_UK",         "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Senegal_FR",                   "France" },
			{ "Wallis_and_Futuna_Islands",    "France" },
			{ "United_States_Virgin_Islands", "United_States_of_America" },
		};

		[MenuItem("Tools/GlobalStrategy/Generate Map Configs")]
		public static void Generate() {
			var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(GeoJsonPath);
			if (textAsset == null) {
				Debug.LogError($"[MapConfigGenerator] GeoJSON not found at {GeoJsonPath}");
				return;
			}

			var features = GeoJsonParser.Parse(textAsset.text);

			if (!AssetDatabase.IsValidFolder("Assets/Configs"))
				AssetDatabase.CreateFolder("Assets", "Configs");

			var featureConfig = LoadOrCreate<MapFeatureConfig>(FeatureConfigPath);
			var countryConfig = LoadOrCreate<CountryConfig>(CountryConfigPath);

			featureConfig.Features.Clear();
			countryConfig.Countries.Clear();

			var genericPattern = new System.Text.RegularExpressions.Regex(@"^feature_\d+$");
			var countryMap = new Dictionary<string, CountryEntry>();

			foreach (var feature in features) {
				if (genericPattern.IsMatch(feature.Name)) continue;

				string normalizedName = NormalizeAscii(feature.Name);
				string mapFeatureId = ToMapFeatureId(normalizedName);

				var entry = new MapFeatureEntry();
				entry.geoJsonId = feature.Name;
				entry.normalizedId = normalizedName;
				entry.mapFeatureId = mapFeatureId;
				featureConfig.Features.Add(entry);

				string partOfNorm = NormalizeAscii(feature.PartOf ?? feature.Name);
				string countryId = ToMapFeatureId(partOfNorm);

				if (!countryMap.TryGetValue(countryId, out var country)) {
					country = new CountryEntry();
					country.countryId = countryId;
					country.displayName = partOfNorm;
					countryMap[countryId] = country;
				}

				bool isMain = feature.Name == feature.PartOf;
				if (isMain)
					country.mainMapFeatureIds.Add(mapFeatureId);
				else
					country.secondaryMapFeatureIds.Add(mapFeatureId);
			}

			// Merge colonial territories into their parent country's secondaryMapFeatureIds
			foreach (var kvp in _colonialParents) {
				string colonyId = kvp.Key;
				string parentId = kvp.Value;
				if (!countryMap.TryGetValue(colonyId, out var colony)) continue;
				if (!countryMap.TryGetValue(parentId, out var parent)) continue;
				parent.secondaryMapFeatureIds.AddRange(colony.mainMapFeatureIds);
				parent.secondaryMapFeatureIds.AddRange(colony.secondaryMapFeatureIds);
				countryMap.Remove(colonyId);
			}

			var sorted = new List<CountryEntry>(countryMap.Values);
			sorted.Sort((a, b) => string.Compare(a.countryId, b.countryId, StringComparison.Ordinal));

			int autoTotal = 0;
			foreach (var c in sorted)
				if (!_predefinedColors.ContainsKey(c.countryId)) autoTotal++;

			int autoIndex = 0;
			foreach (var country in sorted) {
				if (_predefinedColors.TryGetValue(country.countryId, out var col)) {
					country.color = col;
				} else {
					float hue = autoTotal > 1 ? (float)autoIndex / autoTotal : 0f;
					country.color = Color.HSVToRGB(hue, 0.45f, 0.80f);
					autoIndex++;
				}
				countryConfig.Countries.Add(country);
			}

			EditorUtility.SetDirty(featureConfig);
			EditorUtility.SetDirty(countryConfig);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Debug.Log($"[MapConfigGenerator] {featureConfig.Features.Count} features, {countryConfig.Countries.Count} countries.");
		}

		static T LoadOrCreate<T>(string path) where T : ScriptableObject {
			var existing = AssetDatabase.LoadAssetAtPath<T>(path);
			if (existing != null) return existing;
			var obj = ScriptableObject.CreateInstance<T>();
			AssetDatabase.CreateAsset(obj, path);
			return obj;
		}

		static string NormalizeAscii(string s) {
			var sb = new StringBuilder();
			foreach (char c in s) {
				if (c < 128) { sb.Append(c); continue; }
				switch (c) {
					case 'é': case 'è': case 'ê': case 'ë': sb.Append('e'); break;
					case 'à': case 'á': case 'â': case 'ä': sb.Append('a'); break;
					case 'ì': case 'í': case 'î': case 'ï': sb.Append('i'); break;
					case 'ò': case 'ó': case 'ô': case 'ö': sb.Append('o'); break;
					case 'ù': case 'ú': case 'û': case 'ü': sb.Append('u'); break;
					case 'ø': sb.Append('o'); break;
					case 'ñ': sb.Append('n'); break;
					case 'ç': sb.Append('c'); break;
					case 'ß': sb.Append("ss"); break;
				}
			}
			return sb.ToString();
		}

		static string ToMapFeatureId(string normalized) {
			var sb = new StringBuilder();
			bool lastWasUnderscore = false;
			foreach (char c in normalized) {
				if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) {
					sb.Append(c);
					lastWasUnderscore = false;
				} else if (!lastWasUnderscore && sb.Length > 0) {
					sb.Append('_');
					lastWasUnderscore = true;
				}
			}
			return sb.ToString().TrimEnd('_');
		}

		static Color HexColor(int r, int g, int b) =>
			new Color(r / 255f, g / 255f, b / 255f);
	}
}
