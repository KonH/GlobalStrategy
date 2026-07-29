using System.Collections.Generic;
using System.Text.Json.Nodes;
using GS.Game.Configs;

namespace GS.Game.Loader {
	public static class ProvinceProcessor {
		public static (ProvinceConfig metadata, JsonNode geometry, List<string> validationErrors) Process(
			JsonNode intermediate, CountryConfig countryConfig) {
			var provinces = new List<ProvinceEntry>();
			var validationErrors = new List<string>();
			var seenMismatches = new HashSet<string>();

			var featuresArray = intermediate["features"]!.AsArray();
			foreach (var featureNode in featuresArray) {
				if (featureNode == null) {
					continue;
				}
				var props = featureNode["properties"];
				string provinceId = GetStringProp(props, "provinceId") ?? "";
				string countryId = GetStringProp(props, "countryId") ?? "";
				string generationMethod = GetStringProp(props, "generationMethod") ?? "";
				double population = GetDoubleProp(props, "population") ?? 0.0;
				var (centerLon, centerLat) = ComputeCenter(featureNode["geometry"]);

				if (countryConfig.FindByCountryId(countryId) == null && seenMismatches.Add(countryId)) {
					validationErrors.Add(countryId);
				}

				provinces.Add(new ProvinceEntry {
					ProvinceId = provinceId,
					CountryId = countryId,
					GenerationMethod = generationMethod,
					Population = population,
					CenterLon = centerLon,
					CenterLat = centerLat,
				});
			}

			var metadata = new ProvinceConfig { Provinces = provinces };
			return (metadata, intermediate, validationErrors);
		}

		static (double lon, double lat) ComputeCenter(JsonNode? geometry) {
			if (geometry == null) {
				return (0.0, 0.0);
			}

			string? type = geometry["type"]?.GetValue<string>();
			var coordinates = geometry["coordinates"];
			if (type == null || coordinates == null) {
				return (0.0, 0.0);
			}

			double sumLon = 0.0;
			double sumLat = 0.0;
			int count = 0;

			if (type == "Polygon") {
				AccumulateExteriorRing(coordinates.AsArray(), ref sumLon, ref sumLat, ref count);
			} else if (type == "MultiPolygon") {
				foreach (var polygonNode in coordinates.AsArray()) {
					if (polygonNode == null) {
						continue;
					}
					AccumulateExteriorRing(polygonNode.AsArray(), ref sumLon, ref sumLat, ref count);
				}
			}

			if (count == 0) {
				return (0.0, 0.0);
			}
			return (sumLon / count, sumLat / count);
		}

		static void AccumulateExteriorRing(
			JsonArray polygonRings, ref double sumLon, ref double sumLat, ref int count) {
			if (polygonRings.Count == 0) {
				return;
			}
			var exterior = polygonRings[0]?.AsArray();
			if (exterior == null) {
				return;
			}
			foreach (var pointNode in exterior) {
				if (pointNode == null) {
					continue;
				}
				var point = pointNode.AsArray();
				if (point.Count < 2) {
					continue;
				}
				sumLon += point[0]!.GetValue<double>();
				sumLat += point[1]!.GetValue<double>();
				count++;
			}
		}

		static string? GetStringProp(JsonNode? props, string key) {
			if (props == null) {
				return null;
			}
			var val = props[key];
			return val != null ? val.GetValue<string>() : null;
		}

		static double? GetDoubleProp(JsonNode? props, string key) {
			if (props == null) {
				return null;
			}
			var val = props[key];
			return val != null ? val.GetValue<double>() : null;
		}
	}
}
