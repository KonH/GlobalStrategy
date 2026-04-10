using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	public class MapRenderer : MonoBehaviour {
		List<GameObject> _featureObjects = new List<GameObject>();
		Shader _shader;

		void Awake() {
			_shader = Shader.Find("Sprites/Default");
		}

		public void Render(List<MapFeature> features, CountryConfig countryConfig, CountryVisualConfig visualConfig) {
			foreach (var obj in _featureObjects) {
				Destroy(obj);
			}
			_featureObjects.Clear();

			foreach (var feature in features) {
				string mapFeatureId = DeriveMapFeatureId(feature.Name);

				var countryEntry = countryConfig != null ? countryConfig.FindByFeatureId(mapFeatureId) : null;
				if (countryConfig != null && countryEntry == null) {
					continue;
				}

				var go = new GameObject(mapFeatureId);
				go.transform.SetParent(transform, false);

				var mesh = MapMeshBuilder.BuildFeatureMesh(feature);
				if (mesh == null) {
					Destroy(go);
					continue;
				}
				go.AddComponent<MeshFilter>().mesh = mesh;

				Color color = Color.grey;
				color.a = 0.5f;
				if (countryEntry != null && visualConfig != null) {
					var visual = visualConfig.Find(countryEntry.countryId);
					if (visual != null) {
						color = visual.color;
						color.a = 0.5f;
					}
				}

				var mat = new Material(_shader);
				mat.color = color;
				go.AddComponent<MeshRenderer>().material = mat;
				go.AddComponent<FeatureIdentifier>().SetFeature(feature);

				_featureObjects.Add(go);
			}
		}

		public FeatureIdentifier FindFeatureAt(Vector2 worldPos) {
			float lon = worldPos.x / CoordinateConverter.Scale;
			float lat = worldPos.y / CoordinateConverter.Scale;
			foreach (var go in _featureObjects) {
				var id = go.GetComponent<FeatureIdentifier>();
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

		static string DeriveMapFeatureId(string geoJsonName) {
			var ascii = new StringBuilder();
			foreach (char c in geoJsonName) {
				if (c < 128) { ascii.Append(c); continue; }
				switch (c) {
					case 'é': case 'è': case 'ê': case 'ë': ascii.Append('e'); break;
					case 'à': case 'á': case 'â': case 'ä': ascii.Append('a'); break;
					case 'ì': case 'í': case 'î': case 'ï': ascii.Append('i'); break;
					case 'ò': case 'ó': case 'ô': case 'ö': ascii.Append('o'); break;
					case 'ù': case 'ú': case 'û': case 'ü': ascii.Append('u'); break;
					case 'ø': ascii.Append('o'); break;
					case 'ñ': ascii.Append('n'); break;
					case 'ç': ascii.Append('c'); break;
					case 'ß': ascii.Append("ss"); break;
				}
			}
			var id = new StringBuilder();
			bool lastUnderscore = false;
			foreach (char c in ascii.ToString()) {
				if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) {
					id.Append(c);
					lastUnderscore = false;
				} else if (!lastUnderscore && id.Length > 0) {
					id.Append('_');
					lastUnderscore = true;
				}
			}
			if (id.Length > 0 && id[id.Length - 1] == '_') {
				id.Length--;
			}
			return id.ToString();
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

#if UNITY_EDITOR
		void OnDrawGizmos() {
			Gizmos.color = Color.green;
			Gizmos.DrawWireCube(Vector3.zero, new Vector3(CoordinateConverter.MapWidth, CoordinateConverter.MapHeight, 0f));
			if (_featureObjects == null) {
				return;
			}
			Gizmos.color = Color.yellow;
			foreach (var go in _featureObjects) {
				if (go == null) {
					continue;
				}
				var mf = go.GetComponent<MeshFilter>();
				if (mf == null || mf.mesh == null) {
					continue;
				}
				var b = mf.mesh.bounds;
				Gizmos.DrawWireCube(go.transform.TransformPoint(b.center), b.size);
			}
		}
#endif
	}
}
