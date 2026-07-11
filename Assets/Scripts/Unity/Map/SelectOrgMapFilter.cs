using System.Collections.Generic;
using UnityEngine;
using VContainer;
using GS.Game.Configs;
using GS.Main;

namespace GS.Unity.Map {
	public class SelectOrgMapFilter : MonoBehaviour {
		SelectOrgLogic _logic;
		MapController _mapController;
		OrgVisualConfig _orgVisualConfig;
		bool _filtered;

		[Inject]
		void Construct(SelectOrgLogic logic, MapController mapController, OrgVisualConfig orgVisualConfig) {
			_logic = logic;
			_mapController = mapController;
			_orgVisualConfig = orgVisualConfig;
		}

		void Update() {
			if (_filtered) {
				return;
			}
			var renderer = _mapController != null ? _mapController.ActiveProvinceRenderer : null;
			if (renderer == null) {
				return;
			}
			ApplyFilter();
			_filtered = true;
		}

		void ApplyFilter() {
			var countryToOrg = BuildCountryToOrgMap();
			var renderer = _mapController.ActiveProvinceRenderer;
			foreach (var go in renderer.FeatureObjects) {
				if (go == null) {
					continue;
				}
				var identifier = go.GetComponent<ProvinceIdentifier>();
				if (identifier == null) {
					continue;
				}
				bool visible = countryToOrg.TryGetValue(identifier.CountryId, out string orgId);
				go.SetActive(visible);
				if (visible) {
					var mr = go.GetComponent<MeshRenderer>();
					if (mr != null) {
						var c = GetOrgColor(orgId);
						c.a = 0.5f;
						mr.material.color = c;
					}
				}
			}
		}

		Dictionary<string, string> BuildCountryToOrgMap() {
			var result = new Dictionary<string, string>();
			foreach (string hqCountryId in _logic.HqCountryIds) {
				string orgId = _logic.GetOrgIdForHq(hqCountryId);
				result[hqCountryId] = orgId;
			}
			return result;
		}

		Color GetOrgColor(string orgId) {
			var entry = _orgVisualConfig?.Find(orgId);
			if (entry != null) {
				return entry.color;
			}
			int hash = orgId.GetHashCode() & 0x7FFFFFFF;
			float hue = (hash % 1000) / 1000f;
			return Color.HSVToRGB(hue, 0.7f, 0.85f);
		}
	}
}
