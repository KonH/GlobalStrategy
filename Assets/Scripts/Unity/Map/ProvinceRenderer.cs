using System;
using System.Collections.Generic;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class ProvinceRenderer : MonoBehaviour {
		[SerializeField] Material _materialTemplate;
		[SerializeField] Material _borderMaterialTemplate;
		[SerializeField] Material _occupationHatchMaterialTemplate;
		[SerializeField] float _occupationHatchZOffset = -0.01f;
		[SerializeField] int _occupationHatchTextureSize = 32;
		[SerializeField] int _occupationHatchLineWidth = 6;
		[SerializeField] float _occupationHatchTextureScale = 0.04f;
		[SerializeField] float _borderWidth = 0.5f; // Placeholder; tuned visually against the map's world scale later.

		List<GameObject> _featureObjects = new List<GameObject>();
		HashSet<string> _warnedMissingProvinces = new HashSet<string>();
		HashSet<string> _warnedMissingCountryVisuals = new HashSet<string>();
		bool _warnedMissingOccupationHatchMaterial;
		Texture2D _occupationHatchTexture;

		public IReadOnlyList<GameObject> FeatureObjects => _featureObjects;

		public void Render(List<MapFeature> features, GS.Game.Configs.ProvinceConfig provinceConfig, CountryVisualConfig visualConfig) {
			foreach (var obj in _featureObjects) {
				Destroy(obj);
			}
			_featureObjects.Clear();

			foreach (var feature in features) {
				string provinceId = feature.Name;

				var provinceEntry = provinceConfig?.FindByProvinceId(provinceId);
				if (provinceEntry == null) {
					if (_warnedMissingProvinces.Add(provinceId)) {
						Debug.LogWarning($"ProvinceRenderer: no ProvinceEntry found for provinceId '{provinceId}'.");
					}
					continue;
				}

				var go = new GameObject(provinceId);
				go.transform.SetParent(transform, false);

				var mesh = MapMeshBuilder.BuildFeatureMesh(feature);
				if (mesh == null) {
					Destroy(go);
					continue;
				}
				go.AddComponent<MeshFilter>().mesh = mesh;

				Color color = Color.grey;
				color.a = 0.5f;
				if (visualConfig != null) {
					var visual = visualConfig.Find(provinceEntry.CountryId);
					if (visual != null) {
						color = visual.color;
						color.a = 0.5f;
					} else if (_warnedMissingCountryVisuals.Add(provinceEntry.CountryId)) {
						Debug.LogWarning($"ProvinceRenderer: no CountryVisualConfig entry found for countryId '{provinceEntry.CountryId}'.");
					}
				}

				var mat = new Material(_materialTemplate);
				mat.color = color;
				var fillRenderer = go.AddComponent<MeshRenderer>();
				fillRenderer.material = mat;
				fillRenderer.enabled = false;

				go.AddComponent<ProvinceIdentifier>().SetProvince(provinceId, provinceEntry.CountryId, feature);
				CreateOccupationHatch(provinceId, go.transform, mesh);

				var borderMesh = MapMeshBuilder.BuildBorderMesh(feature.Polygons, _borderWidth);
				if (borderMesh != null) {
					var borderGo = new GameObject(provinceId + "_Border");
					borderGo.transform.SetParent(go.transform, false);
					borderGo.AddComponent<MeshFilter>().mesh = borderMesh;
					borderGo.AddComponent<ProvinceBorderRendererMarker>();
					var borderRenderer = borderGo.AddComponent<MeshRenderer>();
					borderRenderer.material = _borderMaterialTemplate;
					borderRenderer.enabled = false;
				}

				_featureObjects.Add(go);
			}
		}

		void CreateOccupationHatch(string provinceId, Transform parent, Mesh mesh) {
			Material template = _occupationHatchMaterialTemplate != null ? _occupationHatchMaterialTemplate : _materialTemplate;
			if (_occupationHatchMaterialTemplate == null && !_warnedMissingOccupationHatchMaterial) {
				Debug.LogWarning("ProvinceRenderer: no occupation hatch material assigned; falling back to province fill material.");
				_warnedMissingOccupationHatchMaterial = true;
			}

			var hatchGo = new GameObject(provinceId + "_OccupationHatch");
			hatchGo.transform.SetParent(parent, false);
			hatchGo.transform.localPosition = new Vector3(0f, 0f, _occupationHatchZOffset);
			hatchGo.AddComponent<MeshFilter>().mesh = mesh;
			hatchGo.AddComponent<ProvinceOccupationHatchMarker>();
			var hatchRenderer = hatchGo.AddComponent<MeshRenderer>();
			var hatchMaterial = new Material(template);
			hatchMaterial.mainTexture = GetOccupationHatchTexture();
			hatchMaterial.mainTextureScale = new Vector2(_occupationHatchTextureScale, _occupationHatchTextureScale);
			hatchRenderer.material = hatchMaterial;
			hatchRenderer.enabled = false;
		}

		Texture2D GetOccupationHatchTexture() {
			if (_occupationHatchTexture != null) {
				return _occupationHatchTexture;
			}

			int size = Mathf.Max(8, _occupationHatchTextureSize);
			int lineWidth = Mathf.Clamp(_occupationHatchLineWidth, 1, size);
			_occupationHatchTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
			_occupationHatchTexture.wrapMode = TextureWrapMode.Repeat;
			_occupationHatchTexture.filterMode = FilterMode.Point;

			for (int y = 0; y < size; y++) {
				for (int x = 0; x < size; x++) {
					int diagonal = (x + y) % size;
					bool onLine = diagonal < lineWidth;
					_occupationHatchTexture.SetPixel(x, y, onLine ? Color.white : new Color(1f, 1f, 1f, 0f));
				}
			}
			_occupationHatchTexture.Apply();
			return _occupationHatchTexture;
		}

		public ProvinceIdentifier FindFeatureAt(Vector2 worldPos) {
			float lon = worldPos.x / CoordinateConverter.Scale;
			float lat = worldPos.y / CoordinateConverter.Scale;
			foreach (var go in _featureObjects) {
				var id = go.GetComponent<ProvinceIdentifier>();
				if (id == null) {
					continue;
				}
				foreach (var polygon in id.Feature.Polygons) {
					if (polygon.Rings.Count == 0) {
						continue;
					}
					if (PointInRing(polygon.Rings[0], lon, lat)) {
						return id;
					}
				}
			}
			return null;
		}

		static bool PointInRing(Ring ring, float lon, float lat) {
			var pts = ring.Points;
			int n = pts.Count;
			if (n > 1) {
				var f = pts[0]; var l = pts[n - 1];
				if (Math.Abs(f.Lon - l.Lon) < 1e-9 && Math.Abs(f.Lat - l.Lat) < 1e-9) {
					n--;
				}
			}
			bool inside = false;
			for (int i = 0, j = n - 1; i < n; j = i++) {
				double xi = pts[i].Lon, yi = pts[i].Lat;
				double xj = pts[j].Lon, yj = pts[j].Lat;
				if (((yi > lat) != (yj > lat)) && (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi)) {
					inside = !inside;
				}
			}
			return inside;
		}
	}
}
