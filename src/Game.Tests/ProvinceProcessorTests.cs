using System.Collections.Generic;
using System.Text.Json.Nodes;
using GS.Game.Configs;
using GS.Game.Loader;
using Xunit;

namespace GS.Game.Tests {
	public class ProvinceProcessorTests {
		static CountryConfig BuildCountryConfig() {
			return new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "France", DisplayName = "France" },
					new CountryEntry { CountryId = "Vatican", DisplayName = "Vatican" },
				}
			};
		}

		static JsonNode BuildFeatureCollection(params (string provinceId, string countryId, string generationMethod)[] entries) {
			var features = new JsonArray();
			foreach (var entry in entries) {
				features.Add(new JsonObject {
					["type"] = "Feature",
					["properties"] = new JsonObject {
						["provinceId"] = entry.provinceId,
						["countryId"] = entry.countryId,
						["generationMethod"] = entry.generationMethod,
					},
					["geometry"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray() },
				});
			}
			return new JsonObject {
				["type"] = "FeatureCollection",
				["features"] = features,
			};
		}

		static JsonNode BuildFeatureCollectionWithPopulation(params (string provinceId, string countryId, double? population)[] entries) {
			var features = new JsonArray();
			foreach (var entry in entries) {
				var properties = new JsonObject {
					["provinceId"] = entry.provinceId,
					["countryId"] = entry.countryId,
					["generationMethod"] = "Micro",
				};
				if (entry.population.HasValue) {
					properties["population"] = entry.population.Value;
				}
				features.Add(new JsonObject {
					["type"] = "Feature",
					["properties"] = properties,
					["geometry"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray() },
				});
			}
			return new JsonObject {
				["type"] = "FeatureCollection",
				["features"] = features,
			};
		}

		[Fact]
		void process_extracts_correct_province_entries() {
			var countryConfig = BuildCountryConfig();
			var doc = BuildFeatureCollection(
				("France__Normandy", "France", "OptionA"),
				("Vatican__Vatican_City", "Vatican", "Micro"));

			var (metadata, geometry, errors) = ProvinceProcessor.Process(doc, countryConfig);

			Assert.Empty(errors);
			Assert.Equal(2, metadata.Provinces.Count);
			var normandy = metadata.FindByProvinceId("France__Normandy");
			Assert.NotNull(normandy);
			Assert.Equal("France", normandy!.CountryId);
			Assert.Equal("OptionA", normandy.GenerationMethod);
			Assert.NotNull(geometry);
		}

		[Fact]
		void process_reports_mismatched_country_id() {
			var countryConfig = BuildCountryConfig();
			var doc = BuildFeatureCollection(
				("Atlantis__Central", "Atlantis", "OptionA"));

			var (metadata, _, errors) = ProvinceProcessor.Process(doc, countryConfig);

			Assert.Single(errors);
			Assert.Contains("Atlantis", errors);
			Assert.Single(metadata.Provinces);
		}

		[Fact]
		void process_extracts_population_field() {
			var countryConfig = BuildCountryConfig();
			var doc = BuildFeatureCollectionWithPopulation(
				("France__Normandy", "France", 12345.5),
				("Vatican__Vatican_City", "Vatican", null));

			var (metadata, _, errors) = ProvinceProcessor.Process(doc, countryConfig);

			Assert.Empty(errors);
			var normandy = metadata.FindByProvinceId("France__Normandy");
			Assert.NotNull(normandy);
			Assert.Equal(12345.5, normandy!.Population);

			var vaticanCity = metadata.FindByProvinceId("Vatican__Vatican_City");
			Assert.NotNull(vaticanCity);
			Assert.Equal(0.0, vaticanCity!.Population);
		}
	}
}
