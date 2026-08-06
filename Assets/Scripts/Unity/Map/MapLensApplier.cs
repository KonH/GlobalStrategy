using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Game.Common;
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
			_state.WorldCountries.PropertyChanged += HandleWorldCountriesChanged;
			_state.ProvinceOwnership.PropertyChanged += HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged += HandleProvinceOccupationChanged;
		}

		void Start() {
			ApplyLens(_state?.MapLens.Lens ?? MapLens.Political, rebuildCountryOrgBorders: true);
		}

		void OnDisable() {
			if (_state == null) {
				return;
			}
			_state.MapLens.PropertyChanged -= HandleLensChanged;
			_state.OrgMap.PropertyChanged  -= HandleOrgMapChanged;
			_state.WorldCountries.PropertyChanged -= HandleWorldCountriesChanged;
			_state.ProvinceOwnership.PropertyChanged -= HandleProvinceOwnershipChanged;
			_state.ProvinceOccupation.PropertyChanged -= HandleProvinceOccupationChanged;
		}

		void HandleLensChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens, rebuildCountryOrgBorders: true);
		}

		void HandleOrgMapChanged(object sender, PropertyChangedEventArgs e) {
			if (_state.MapLens.Lens == MapLens.Org) {
				ApplyLens(MapLens.Org, rebuildCountryOrgBorders: true);
			}
		}

		void HandleWorldCountriesChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens, rebuildCountryOrgBorders: true);
		}

		void HandleProvinceOwnershipChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens, rebuildCountryOrgBorders: true);
		}

		void HandleProvinceOccupationChanged(object sender, PropertyChangedEventArgs e) {
			ApplyLens(_state.MapLens.Lens, rebuildCountryOrgBorders: false);
		}

		void ApplyLens(MapLens lens, bool rebuildCountryOrgBorders) {
			if (_mapController == null) {
				return;
			}

			bool showBorders = lens == MapLens.Province;
			bool showCountryOrgBorders = lens == MapLens.Political || lens == MapLens.Org;
			var ownerByProvinceIdResolved = new Dictionary<string, string>();
			var visibleProvinceIds = new HashSet<string>();

			foreach (var provinceRenderer in _mapController.ProvinceRenderers) {
				if (provinceRenderer == null) {
					continue;
				}

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
					string occupierId = ResolveOccupier(identifier.ProvinceId);
					bool inWorld = IsCountryInWorld(ownerId);
					bool visiblyOccupied = inWorld && occupierId != "" && occupierId != ownerId;

					ownerByProvinceIdResolved[identifier.ProvinceId] = ownerId;
					if (inWorld) {
						visibleProvinceIds.Add(identifier.ProvinceId);
					}

					fillRenderer.enabled = inWorld;
					SetBorderRenderersEnabled(go, inWorld && showBorders);
					SetOccupationHatchEnabled(go, visiblyOccupied, GetOccupationColor(occupierId));

					if (!inWorld) {
						continue;
					}
					fillRenderer.material.color = GetColor(lens, ownerId);
				}

				if (showCountryOrgBorders) {
					if (rebuildCountryOrgBorders) {
						provinceRenderer.RebuildCountryOrgBorders(
							lens,
							ownerByProvinceIdResolved,
							BuildTopOrgLookup(),
							visibleProvinceIds);
					}
				} else {
					provinceRenderer.DisableCountryOrgBorders();
				}
			}
		}

		Dictionary<string, string> BuildTopOrgLookup() {
			var result = new Dictionary<string, string>();
			var entries = _state?.OrgMap?.Entries;
			if (entries == null) {
				return result;
			}
			foreach (var e in entries) {
				if (string.IsNullOrEmpty(e.CountryId) || string.IsNullOrEmpty(e.TopOrgId)) {
					continue;
				}
				result[e.CountryId] = e.TopOrgId;
			}
			return result;
		}

		string ResolveOwner(ProvinceIdentifier identifier) {
			var owners = _state?.ProvinceOwnership?.OwnerByProvinceId;
			if (owners != null && owners.TryGetValue(identifier.ProvinceId, out string ownerId)) {
				return ownerId;
			}
			return identifier.CountryId;
		}

		string ResolveOccupier(string provinceId) {
			var occupiers = _state?.ProvinceOccupation?.OccupierByProvinceId;
			if (occupiers != null && occupiers.TryGetValue(provinceId, out string occupierId)) {
				return occupierId ?? "";
			}
			return "";
		}

		static void SetBorderRenderersEnabled(GameObject fillGo, bool enabled) {
			foreach (Transform child in fillGo.transform) {
				if (child.GetComponent<ProvinceBorderRendererMarker>() == null) {
					continue;
				}
				var childRenderer = child.GetComponent<MeshRenderer>();
				if (childRenderer != null) {
					childRenderer.enabled = enabled;
				}
			}
		}

		static void SetOccupationHatchEnabled(GameObject fillGo, bool enabled, Color color) {
			foreach (Transform child in fillGo.transform) {
				if (child.GetComponent<ProvinceOccupationHatchMarker>() == null) {
					continue;
				}
				var childRenderer = child.GetComponent<MeshRenderer>();
				if (childRenderer == null) {
					continue;
				}
				childRenderer.enabled = enabled;
				if (enabled) {
					childRenderer.material.color = color;
				}
			}
		}

		bool IsCountryInWorld(string countryId) {
			var ids = _state?.WorldCountries?.CountryIds;
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

		Color GetOccupationColor(string countryId) {
			var c = GetPoliticalColor(countryId);
			c.a = 0.8f;
			return c;
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
