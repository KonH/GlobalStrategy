using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using GS.Configs.IO;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ProvinceConfigTests {
		static ProvinceConfig BuildConfig() {
			return new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry {
						ProvinceId = "France__Ile_de_France",
						CountryId = "France",
						GenerationMethod = "OptionA"
					},
					new ProvinceEntry {
						ProvinceId = "France__Normandy",
						CountryId = "France",
						GenerationMethod = "OptionA"
					},
					new ProvinceEntry {
						ProvinceId = "Vatican__Vatican_City",
						CountryId = "Vatican",
						GenerationMethod = "Micro"
					}
				}
			};
		}

		[Fact]
		void find_by_province_id_returns_correct_entry() {
			var config = BuildConfig();
			var entry = config.FindByProvinceId("France__Normandy");
			Assert.NotNull(entry);
			Assert.Equal("France", entry!.CountryId);
		}

		[Fact]
		void find_by_province_id_returns_null_for_unknown() {
			var config = BuildConfig();
			Assert.Null(config.FindByProvinceId("Unknown__Province"));
		}

		[Fact]
		void find_by_country_id_returns_all_matching_entries() {
			var config = BuildConfig();
			var entries = config.FindByCountryId("France");
			Assert.Equal(2, entries.Count);
		}

		[Fact]
		void find_by_country_id_returns_empty_list_for_unrepresented_country() {
			var config = BuildConfig();
			var entries = config.FindByCountryId("Germany");
			Assert.NotNull(entries);
			Assert.Empty(entries);
		}

		[Fact]
		void entry_fields_are_correct() {
			var config = BuildConfig();
			var entry = config.FindByProvinceId("Vatican__Vatican_City");
			Assert.NotNull(entry);
			Assert.Equal("Vatican", entry!.CountryId);
			Assert.Equal("Micro", entry.GenerationMethod);
		}

		[Fact]
		void committed_config_has_finite_symmetric_ordinal_topology() {
			string path = FindRepositoryFile("Assets/Configs/province_config.json");
			ProvinceConfig config = new FileConfig<ProvinceConfig>(path).Load();
			var byId = config.Provinces.ToDictionary(
				entry => entry.ProvinceId, StringComparer.Ordinal);

			Assert.NotEmpty(config.Provinces);
			foreach (ProvinceEntry entry in config.Provinces) {
				Assert.True(double.IsFinite(entry.CentroidX));
				Assert.True(double.IsFinite(entry.CentroidY));
				Assert.DoesNotContain(entry.ProvinceId, entry.NeighborProvinceIds);
				Assert.Equal(
					entry.NeighborProvinceIds.OrderBy(id => id, StringComparer.Ordinal),
					entry.NeighborProvinceIds);
				Assert.Equal(entry.NeighborProvinceIds.Count, entry.NeighborProvinceIds.Distinct().Count());
				foreach (string neighborId in entry.NeighborProvinceIds) {
					Assert.True(byId.ContainsKey(neighborId), $"Unknown neighbor '{neighborId}'.");
					Assert.Contains(entry.ProvinceId, byId[neighborId].NeighborProvinceIds);
				}
			}
		}

		static string FindRepositoryFile(string relativePath) {
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory != null) {
				string candidate = Path.Combine(directory.FullName, relativePath);
				if (File.Exists(candidate)) {
					return candidate;
				}
				directory = directory.Parent;
			}
			throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
		}
	}
}
