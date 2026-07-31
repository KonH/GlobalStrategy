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
				double centroidX = GetDoubleProp(props, "centroidX") ?? 0.0;
				double centroidY = GetDoubleProp(props, "centroidY") ?? 0.0;
				bool isMainTerritory = GetBoolProp(props, "isMainTerritory") ?? true;
				var neighborProvinceIds = GetStringArrayProp(props, "neighborProvinceIds");

				if (countryConfig.FindByCountryId(countryId) == null && seenMismatches.Add(countryId)) {
					validationErrors.Add(countryId);
				}

				provinces.Add(new ProvinceEntry {
					ProvinceId = provinceId,
					CountryId = countryId,
					GenerationMethod = generationMethod,
					Population = population,
					CentroidX = centroidX,
					CentroidY = centroidY,
					IsMainTerritory = isMainTerritory,
					NeighborProvinceIds = neighborProvinceIds,
				});
			}

			var metadata = new ProvinceConfig { Provinces = provinces };
			return (metadata, intermediate, validationErrors);
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

		static bool? GetBoolProp(JsonNode? props, string key) {
			if (props == null) {
				return null;
			}
			var val = props[key];
			return val != null ? val.GetValue<bool>() : null;
		}

		static List<string> GetStringArrayProp(JsonNode? props, string key) {
			var result = new List<string>();
			if (props?[key] is not JsonArray values) {
				return result;
			}
			foreach (JsonNode? value in values) {
				if (value != null) {
					result.Add(value.GetValue<string>());
				}
			}
			return result;
		}
	}
}
