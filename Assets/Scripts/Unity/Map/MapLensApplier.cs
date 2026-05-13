using System.ComponentModel;
using UnityEngine;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Configs;

namespace GS.Unity.Map {
	public class MapLensApplier : MonoBehaviour {
		VisualState _state;
		MapController _mapController;
		CountryVisualConfig _visualConfig;
		OrgVisualConfig _orgVisualConfig;
		CountryConfig _domainCountryConfig;

		[Inject]
		void Construct(VisualState state, MapController mapController, CountryVisualConfig visualConfig, OrgVisualConfig orgVisualConfig, CountryConfig domainCountryConfig) {
			_state = state;
			_mapController = mapController;
			_visualConfig = visualConfig;
			_orgVisualConfig = orgVisualConfig;
			_domainCountryConfig = domainCountryConfig;
		}

		void OnEnable() {
			if (_state == null) {
				return;
			}
			_state.MapLens.PropertyChanged += HandleLensChanged;
			_state.OrgMap.PropertyChanged  += HandleOrgMapChanged;
		}

		void Start() {
			ApplyLens(_state?.MapLens.Lens ?? MapLens.Political);
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			_state.MapLens.PropertyChanged -= HandleLensChanged;
			_state.OrgMap.PropertyChanged  -= HandleOrgMapChanged;
		}

		void HandleLensChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens);
		}

		void HandleOrgMapChanged(object sender, PropertyChangedEventArgs e) {
			if (_state.MapLens.Lens == MapLens.Org) {
				ApplyLens(MapLens.Org);
			}
		}

		void ApplyLens(MapLens lens) {
			var renderer = _mapController?.ActiveRenderer;
			if (renderer == null) {
				return;
			}
			foreach (var go in renderer.FeatureObjects) {
				if (go == null) {
					continue;
				}
				var mr = go.GetComponent<MeshRenderer>();
				if (mr == null) {
					continue;
				}
				mr.material.color = GetColor(lens, go.name);
			}
		}

		Color GetColor(MapLens lens, string countryId) {
			switch (lens) {
				case MapLens.Geographic:
					return new Color(0, 0, 0, 0);
				case MapLens.Org:
					return GetOrgColor(countryId);
				default:
					return GetPoliticalColor(countryId);
			}
		}

		Color GetPoliticalColor(string mapFeatureId) {
			var country = _domainCountryConfig?.FindByFeatureId(mapFeatureId);
			string domainId = country != null ? country.CountryId : mapFeatureId;
			var entry = _visualConfig?.Find(domainId);
			if (entry == null) {
				return new Color(0.5f, 0.5f, 0.5f, 0.5f);
			}
			var c = entry.color;
			c.a = 0.5f;
			return c;
		}

		Color GetOrgColor(string mapFeatureId) {
			var country = _domainCountryConfig?.FindByFeatureId(mapFeatureId);
			string domainId = country != null ? country.CountryId : mapFeatureId;
			foreach (var e in _state.OrgMap.Entries) {
				if (e.CountryId != domainId) {
					continue;
				}
				var c = OrgIdToColor(e.TopOrgId);
				c.a = 0.35f + 0.45f * e.InfluenceRatio;
				return c;
			}
			return new Color(0, 0, 0, 0);
		}

		Color OrgIdToColor(string orgId) {
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
