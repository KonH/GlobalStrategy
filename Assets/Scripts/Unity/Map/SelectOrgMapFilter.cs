using System.Collections.Generic;
using UnityEngine;
using VContainer;
using GS.Main;

namespace GS.Unity.Map {
	public class SelectOrgMapFilter : MonoBehaviour {
		SelectOrgLogic _logic;
		CountryConfig _countryConfig;
		MapController _mapController;
		bool _filtered;

		[Inject]
		void Construct(SelectOrgLogic logic, CountryConfig countryConfig, MapController mapController) {
			_logic = logic;
			_countryConfig = countryConfig;
			_mapController = mapController;
		}

		void Update() {
			if (_filtered) {
				return;
			}
			var renderer = _mapController != null ? _mapController.ActiveRenderer : null;
			if (renderer == null) {
				return;
			}
			ApplyFilter();
			_filtered = true;
		}

		void ApplyFilter() {
			var hqFeatureIds = BuildHqFeatureIds();
			foreach (var renderer in Object.FindObjectsOfType<MapRenderer>()) {
				foreach (var go in renderer.FeatureObjects) {
					if (go == null) {
						continue;
					}
					go.SetActive(hqFeatureIds.Contains(go.name));
				}
			}
		}

		HashSet<string> BuildHqFeatureIds() {
			var result = new HashSet<string>();
			foreach (string hqCountryId in _logic.HqCountryIds) {
				var entry = FindCountryEntry(hqCountryId);
				if (entry == null) {
					continue;
				}
				foreach (var id in entry.mainMapFeatureIds) {
					result.Add(id);
				}
				foreach (var id in entry.secondaryMapFeatureIds) {
					result.Add(id);
				}
			}
			return result;
		}

		CountryEntry FindCountryEntry(string countryId) {
			if (_countryConfig == null) {
				return null;
			}
			foreach (var entry in _countryConfig.Countries) {
				if (entry.countryId == countryId) {
					return entry;
				}
			}
			return null;
		}
	}
}
