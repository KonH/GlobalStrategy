using System;
using System.Collections.Generic;
using System.Text;

namespace GS.Core.Map {
	public static class GeoJsonParser {
		static readonly string[] _nameKeys = { "NAME", "name", "ADMIN", "admin", "NAME_LONG", "SOVEREIGNT" };

		public static List<MapFeature> Parse(string json) {
			var features = new List<MapFeature>();
			if (!((Parser.Parse(json)) is Dictionary<string, object> root)) return features;
			if (!root.TryGetValue("features", out var arr) || !(arr is List<object> featureArray)) return features;

			for (int i = 0; i < featureArray.Count; i++) {
				if (!(featureArray[i] is Dictionary<string, object> el)) continue;
				var feature = ParseFeature(el, i);
				if (feature != null) features.Add(feature);
			}
			return features;
		}

		static MapFeature ParseFeature(Dictionary<string, object> el, int fallbackIndex) {
			string name = null;
			string partOf = null;
			if (el.TryGetValue("properties", out var propsObj) && propsObj is Dictionary<string, object> props) {
				foreach (var key in _nameKeys) {
					if (props.TryGetValue(key, out var v) && v is string s) { name = s; break; }
				}
				if (props.TryGetValue("PARTOF", out var pv) && pv is string ps) partOf = ps;
			}
			string id = name ?? $"feature_{fallbackIndex}";

			if (!el.TryGetValue("geometry", out var geomObj) || !(geomObj is Dictionary<string, object> geom))
				return null;

			var polygons = ParseGeometry(geom);
			if (polygons == null || polygons.Count == 0) return null;

			return new MapFeature(id, name ?? id, polygons) { PartOf = partOf ?? (name ?? id) };
		}

		static List<Polygon> ParseGeometry(Dictionary<string, object> geom) {
			if (!geom.TryGetValue("type", out var typeObj) || !(typeObj is string type)) return null;
			if (!geom.TryGetValue("coordinates", out var coordsObj)) return null;

			var polygons = new List<Polygon>();
			if (type == "Polygon" && coordsObj is List<object> polyrings) {
				var poly = ParsePolygon(polyrings);
				if (poly != null) polygons.Add(poly);
			} else if (type == "MultiPolygon" && coordsObj is List<object> multiCoords) {
				foreach (var polyObj in multiCoords) {
					if (!(polyObj is List<object> rings)) continue;
					var poly = ParsePolygon(rings);
					if (poly != null) polygons.Add(poly);
				}
			}
			return polygons;
		}

		static Polygon ParsePolygon(List<object> rings) {
			var result = new List<Ring>();
			foreach (var ringObj in rings) {
				if (!(ringObj is List<object> coords)) continue;
				var ring = ParseRing(coords);
				if (ring != null) result.Add(ring);
			}
			return result.Count > 0 ? new Polygon(result) : null;
		}

		static Ring ParseRing(List<object> coords) {
			var points = new List<Vector2d>();
			foreach (var ptObj in coords) {
				if (!(ptObj is List<object> pt) || pt.Count < 2) continue;
				double lon = Convert.ToDouble(pt[0]);
				double lat = Convert.ToDouble(pt[1]);
				points.Add(new Vector2d(lon, lat));
			}
			return points.Count >= 3 ? new Ring(points) : null;
		}

		// Minimal recursive-descent JSON parser
		// Returns: Dictionary<string,object>, List<object>, string, double, bool, null
		static class Parser {
			public static object Parse(string json) => new State(json).ParseValue();

			class State {
				readonly string _s;
				int _i;

				public State(string s) { _s = s; }

				public object ParseValue() {
					Skip();
					if (_i >= _s.Length) return null;
					char c = _s[_i];
					if (c == '{') return ParseObj();
					if (c == '[') return ParseArr();
					if (c == '"') return ParseStr();
					if (c == 't') { _i += 4; return (object)true; }
					if (c == 'f') { _i += 5; return (object)false; }
					if (c == 'n') { _i += 4; return null; }
					return ParseNum();
				}

				Dictionary<string, object> ParseObj() {
					_i++; Skip();
					var d = new Dictionary<string, object>();
					while (_i < _s.Length && _s[_i] != '}') {
						var key = ParseStr(); Skip(); _i++; // colon
						d[key] = ParseValue();
						Skip();
						if (_i < _s.Length && _s[_i] == ',') _i++;
						Skip();
					}
					if (_i < _s.Length) _i++;
					return d;
				}

				List<object> ParseArr() {
					_i++; Skip();
					var list = new List<object>();
					while (_i < _s.Length && _s[_i] != ']') {
						list.Add(ParseValue());
						Skip();
						if (_i < _s.Length && _s[_i] == ',') _i++;
						Skip();
					}
					if (_i < _s.Length) _i++;
					return list;
				}

				string ParseStr() {
					_i++;
					var sb = new StringBuilder();
					while (_i < _s.Length && _s[_i] != '"') {
						if (_s[_i] == '\\') {
							_i++;
							char esc = _s[_i];
							if (esc == 'n') sb.Append('\n');
							else if (esc == 'r') sb.Append('\r');
							else if (esc == 't') sb.Append('\t');
							else sb.Append(esc);
						} else {
							sb.Append(_s[_i]);
						}
						_i++;
					}
					if (_i < _s.Length) _i++;
					return sb.ToString();
				}

				object ParseNum() {
					int start = _i;
					while (_i < _s.Length) {
						char c = _s[_i];
						if (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || char.IsDigit(c))
							_i++;
						else
							break;
					}
					return double.Parse(_s.Substring(start, _i - start), System.Globalization.CultureInfo.InvariantCulture);
				}

				void Skip() {
					while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
				}
			}
		}
	}
}
