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

		[Inject]
		void Construct(VisualState state, MapController mapController, CountryVisualConfig visualConfig, OrgVisualConfig orgVisualConfig) {
			_state = state;
			_mapController = mapController;
			_visualConfig = visualConfig;
			_orgVisualConfig = orgVisualConfig;
		}

		void OnEnable() {
			if (_state == null) {
				return;
			}
			_state.MapLens.PropertyChanged += HandleLensChanged;
			_state.OrgMap.PropertyChanged  += HandleOrgMapChanged;
			_state.DiscoveredCountries.PropertyChanged += HandleDiscoveredChanged;
			_state.ProvinceOwnership.PropertyChanged += HandleProvinceOwnershipChanged;
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
			_state.DiscoveredCountries.PropertyChanged -= HandleDiscoveredChanged;
			_state.ProvinceOwnership.PropertyChanged -= HandleProvinceOwnershipChanged;
		}

		void HandleLensChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens);
		}

		void HandleOrgMapChanged(object sender, PropertyChangedEventArgs e) {
			if (_state.MapLens.Lens == MapLens.Org) {
				ApplyLens(MapLens.Org);
			}
		}

		void HandleDiscoveredChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens);
		}

		void HandleProvinceOwnershipChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens);
		}

		void ApplyLens(MapLens lens) {
			var provinceRenderer = _mapController?.ActiveProvinceRenderer;
			if (provinceRenderer == null) {
				return;
			}

			bool showBorders = lens == MapLens.Province;

			foreach (var go in provinceRenderer.FeatureObjects) {
				if (go == null) {
					continue;
				}
				var identifier = go.GetComponent<ProvinceIdentifier>();
				var fillRenderer = go.GetComponent<MeshRenderer>();
				if (identifier == null || fillRenderer == null) {
					continue;
				}

				string ownerId = ResolveOwner(identifier);
				bool discovered = IsCountryDiscovered(ownerId);

				fillRenderer.enabled = discovered;
				SetBorderRenderersEnabled(go, discovered && showBorders);

				if (!discovered) {
					continue;
				}
				fillRenderer.material.color = GetColor(lens, ownerId);
			}
		}

		string ResolveOwner(ProvinceIdentifier identifier) {
			var owners = _state?.ProvinceOwnership?.OwnerByProvinceId;
			if (owners != null && owners.TryGetValue(identifier.ProvinceId, out string ownerId)) {
				return ownerId;
			}
			return identifier.CountryId;
		}

		static void SetBorderRenderersEnabled(GameObject fillGo, bool enabled) {
			foreach (Transform child in fillGo.transform) {
				var childRenderer = child.GetComponent<MeshRenderer>();
				if (childRenderer != null) {
					childRenderer.enabled = enabled;
				}
			}
		}

		bool IsCountryDiscovered(string countryId) {
			var ids = _state?.DiscoveredCountries?.CountryIds;
			if (ids == null) { return true; }
			return ids.Contains(countryId);
		}

		Color GetColor(MapLens lens, string ownerId) {
			switch (lens) {
				case MapLens.Geographic:
					return new Color(0, 0, 0, 0);
				case MapLens.Org:
					return GetOrgColor(ownerId);
				default:
					return GetPoliticalColor(ownerId);
			}
		}

		Color GetPoliticalColor(string ownerId) {
			var entry = _visualConfig?.Find(ownerId);
			if (entry == null) {
				return new Color(0.5f, 0.5f, 0.5f, 0.5f);
			}
			var c = entry.color;
			c.a = 0.5f;
			return c;
		}

		Color GetOrgColor(string ownerId) {
			foreach (var e in _state.OrgMap.Entries) {
				if (e.CountryId != ownerId) {
					continue;
				}
				var c = OrgIdToColor(e.TopOrgId);
				c.a = 0.35f + 0.45f * e.ControlRatio;
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
