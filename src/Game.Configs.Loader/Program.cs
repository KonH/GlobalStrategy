using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GS.Game.Configs;

namespace GS.Game.Loader {
	static class Program {
		static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions {
			WriteIndented = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		};

		static readonly Dictionary<string, string> _colonialParents = new Dictionary<string, string> {
			{ "Gold_Coast_GB",                "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Hong_Kong",                    "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "New_South_Wales_UK",           "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Northern_Territory_UK",        "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Queensland_UK",                "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "South_Australia_UK",           "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Western_Australia_UK",         "United_Kingdom_of_Great_Britain_and_Ireland" },
			{ "Senegal_FR",                   "France" },
			{ "Wallis_and_Futuna_Islands",     "France" },
			{ "United_States_Virgin_Islands",  "United_States_of_America" },
		};

		static void Main() {
			var loaderConfig = JsonSerializer.Deserialize<LoaderConfig>(
				File.ReadAllText("loader_config.json"))!;

			Console.WriteLine($"Reading GeoJSON from: {loaderConfig.GeoJsonSourcePath}");
			string rawJson = File.ReadAllText(loaderConfig.GeoJsonSourcePath);

			var (features, mapEntries, countryConfig, geoJsonConfig) = ProcessGeoJson(rawJson);

			string outputDir = loaderConfig.OutputPath;
			Directory.CreateDirectory(outputDir);

			File.WriteAllText(
				Path.Combine(outputDir, "geojson_world.json"),
				JsonSerializer.Serialize(geoJsonConfig, _writeOptions));

			File.WriteAllText(
				Path.Combine(outputDir, "map_entry_config.json"),
				JsonSerializer.Serialize(mapEntries, _writeOptions));

			File.WriteAllText(
				Path.Combine(outputDir, "country_config.json"),
				JsonSerializer.Serialize(countryConfig, _writeOptions));

			Console.WriteLine($"Wrote {mapEntries.Features.Count} features, {countryConfig.Countries.Count} countries to {outputDir}");
		}

		static (List<string> features, MapEntryConfig mapEntries, CountryConfig countryConfig, GeoJsonConfig geoJsonConfig)
			ProcessGeoJson(string rawJson) {
			var doc = JsonNode.Parse(rawJson)!;
			var featuresArray = doc["features"]!.AsArray();

			var genericPattern = new System.Text.RegularExpressions.Regex(@"^feature_\d+$");
			var mapEntries = new MapEntryConfig();
			var countryMap = new Dictionary<string, CountryEntry>();
			var geoJsonFeatures = new List<GeoJsonFeatureConfig>();
			var featureNames = new List<string>();
			int fallbackIndex = 0;

			foreach (var featureNode in featuresArray) {
				if (featureNode == null) { fallbackIndex++; continue; }
				var props = featureNode["properties"];
				string? name = GetStringProp(props, "NAME", "name", "ADMIN", "admin", "NAME_LONG", "SOVEREIGNT");
				string? partOf = GetStringProp(props, "PARTOF");
				string featureName = name ?? $"feature_{fallbackIndex}";
				fallbackIndex++;

				if (genericPattern.IsMatch(featureName)) continue;

				string normalizedName = NormalizeAscii(featureName);
				string mapFeatureId = ToMapFeatureId(normalizedName);

				mapEntries.Features.Add(new MapFeatureEntry {
					GeoJsonId = featureName,
					NormalizedId = normalizedName,
					MapFeatureId = mapFeatureId,
				});

				string partOfNorm = NormalizeAscii(partOf ?? featureName);
				string countryId = ToMapFeatureId(partOfNorm);

				if (!countryMap.TryGetValue(countryId, out var country)) {
					country = new CountryEntry {
						CountryId = countryId,
						DisplayName = partOfNorm,
					};
					countryMap[countryId] = country;
				}

				bool isMain = partOf == null || featureName == partOf;
				if (isMain)
					country.MainMapFeatureIds.Add(mapFeatureId);
				else
					country.SecondaryMapFeatureIds.Add(mapFeatureId);

				geoJsonFeatures.Add(new GeoJsonFeatureConfig {
					Name = featureName,
					PartOf = partOf ?? featureName,
				});
				featureNames.Add(featureName);
			}

			// Merge colonial territories
			foreach (var kvp in _colonialParents) {
				if (!countryMap.TryGetValue(kvp.Key, out var colony)) continue;
				if (!countryMap.TryGetValue(kvp.Value, out var parent)) continue;
				parent.SecondaryMapFeatureIds.AddRange(colony.MainMapFeatureIds);
				parent.SecondaryMapFeatureIds.AddRange(colony.SecondaryMapFeatureIds);
				countryMap.Remove(kvp.Key);
			}

			var sorted = new List<CountryEntry>(countryMap.Values);
			sorted.Sort((a, b) => string.Compare(a.CountryId, b.CountryId, StringComparison.Ordinal));

			var countryConfig = new CountryConfig { Countries = sorted };
			var geoJsonConfig = new GeoJsonConfig { Features = geoJsonFeatures };
			return (featureNames, mapEntries, countryConfig, geoJsonConfig);
		}

		static string? GetStringProp(JsonNode? props, params string[] keys) {
			if (props == null) return null;
			foreach (var key in keys) {
				var val = props[key];
				if (val != null) return val.GetValue<string>();
			}
			return null;
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
	}

	class LoaderConfig {
		public string GeoJsonSourcePath { get; set; } = "";
		public string OutputPath { get; set; } = "";
	}
}
