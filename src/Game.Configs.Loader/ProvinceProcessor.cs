using System.Collections.Generic;
using System.Text.Json.Nodes;
using GS.Game.Configs;

namespace GS.Game.Loader {
	static class ProvinceProcessor {
		internal static (ProvinceConfig metadata, JsonNode geometry, List<string> validationErrors) Process(
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
				string displayName = GetStringProp(props, "displayName") ?? "";
				string generationMethod = GetStringProp(props, "generationMethod") ?? "";

				if (countryConfig.FindByCountryId(countryId) == null && seenMismatches.Add(countryId)) {
					validationErrors.Add(countryId);
				}

				provinces.Add(new ProvinceEntry {
					ProvinceId = provinceId,
					CountryId = countryId,
					DisplayName = displayName,
					GenerationMethod = generationMethod,
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
	}
}
