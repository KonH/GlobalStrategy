using System;
using System.Collections.Generic;

namespace GS.Core.Map {
	public static class BorderSegmentIndex {
		public static Dictionary<string, string[][]> BuildNeighborMap(
			IReadOnlyDictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId,
			IReadOnlyDictionary<string, IReadOnlyList<string>> neighborProvinceIdsByProvinceId,
			double epsilon = 0.01,
			double proximityThreshold = 0.05) {
			var result = new Dictionary<string, string[][]>(ringsByProvinceId.Count);
			foreach (var kvp in ringsByProvinceId) {
				var rings = kvp.Value;
				var perRing = new string[rings.Count][];
				for (int r = 0; r < rings.Count; r++) {
					var ring = rings[r];
					int n = ring != null ? ring.Length : 0;
					var segments = new string[n];
					for (int i = 0; i < n; i++) {
						segments[i] = "";
					}
					perRing[r] = segments;
				}
				result[kvp.Key] = perRing;
			}

			var neighborSets = new Dictionary<string, HashSet<string>>(neighborProvinceIdsByProvinceId.Count);
			foreach (var kvp in neighborProvinceIdsByProvinceId) {
				neighborSets[kvp.Key] = new HashSet<string>(kvp.Value ?? Array.Empty<string>());
			}

			AttributeExactMatches(ringsByProvinceId, neighborSets, result, epsilon);
			AttributeProximityFallback(
				ringsByProvinceId, neighborSets, result, proximityThreshold);
			return result;
		}

		static void AttributeExactMatches(
			IReadOnlyDictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId,
			IReadOnlyDictionary<string, HashSet<string>> neighborSets,
			Dictionary<string, string[][]> result,
			double epsilon) {
			var byKey = new Dictionary<(long, long, long, long), List<(string ProvinceId, int Ring, int Segment)>>();

			foreach (var kvp in ringsByProvinceId) {
				string provinceId = kvp.Key;
				var rings = kvp.Value;
				for (int r = 0; r < rings.Count; r++) {
					var ring = rings[r];
					if (ring == null || ring.Length < 2) {
						continue;
					}
					int n = ring.Length;
					for (int i = 0; i < n; i++) {
						var a = ring[i];
						var b = ring[(i + 1) % n];
						var key = QuantizedEdgeKey(a, b, epsilon);
						if (!byKey.TryGetValue(key, out var list)) {
							list = new List<(string, int, int)>();
							byKey[key] = list;
						}
						list.Add((provinceId, r, i));
					}
				}
			}

			foreach (var kvp in byKey) {
				var occurrences = kvp.Value;
				var distinctProvinces = new List<string>();
				foreach (var occ in occurrences) {
					if (!distinctProvinces.Contains(occ.ProvinceId)) {
						distinctProvinces.Add(occ.ProvinceId);
					}
				}

				if (distinctProvinces.Count < 2) {
					continue;
				}

				foreach (var occ in occurrences) {
					if (result[occ.ProvinceId][occ.Ring][occ.Segment] != "") {
						continue;
					}
					if (!neighborSets.TryGetValue(occ.ProvinceId, out var myNeighbors)) {
						continue;
					}

					string best = null;
					foreach (var other in distinctProvinces) {
						if (other == occ.ProvinceId) {
							continue;
						}
						if (!myNeighbors.Contains(other)) {
							continue;
						}
						if (best == null || string.CompareOrdinal(other, best) < 0) {
							best = other;
						}
					}
					if (best != null) {
						result[occ.ProvinceId][occ.Ring][occ.Segment] = best;
					}
				}
			}
		}

		static void AttributeProximityFallback(
			IReadOnlyDictionary<string, IReadOnlyList<Vector2d[]>> ringsByProvinceId,
			IReadOnlyDictionary<string, HashSet<string>> neighborSets,
			Dictionary<string, string[][]> result,
			double proximityThreshold) {
			foreach (var kvp in ringsByProvinceId) {
				string provinceId = kvp.Key;
				var rings = kvp.Value;
				if (!neighborSets.TryGetValue(provinceId, out var myNeighbors) || myNeighbors.Count == 0) {
					continue;
				}

				for (int r = 0; r < rings.Count; r++) {
					var ring = rings[r];
					if (ring == null || ring.Length < 2) {
						continue;
					}
					int n = ring.Length;
					for (int i = 0; i < n; i++) {
						if (result[provinceId][r][i] != "") {
							continue;
						}

						var a = ring[i];
						var b = ring[(i + 1) % n];
						var mid = new Vector2d((a.Lon + b.Lon) * 0.5, (a.Lat + b.Lat) * 0.5);

						string bestNeighbor = null;
						double bestDist = double.PositiveInfinity;
						foreach (var neighborId in myNeighbors) {
							if (!ringsByProvinceId.TryGetValue(neighborId, out var neighborRings)) {
								continue;
							}
							double dist = MinDistanceToRings(mid, neighborRings);
							if (dist > proximityThreshold) {
								continue;
							}
							if (bestNeighbor == null
								|| dist < bestDist
								|| (dist == bestDist && string.CompareOrdinal(neighborId, bestNeighbor) < 0)) {
								bestDist = dist;
								bestNeighbor = neighborId;
							}
						}
						if (bestNeighbor != null) {
							result[provinceId][r][i] = bestNeighbor;
						}
					}
				}
			}
		}

		static double MinDistanceToRings(Vector2d point, IReadOnlyList<Vector2d[]> rings) {
			double best = double.PositiveInfinity;
			for (int r = 0; r < rings.Count; r++) {
				var ring = rings[r];
				if (ring == null || ring.Length < 2) {
					continue;
				}
				int n = ring.Length;
				for (int i = 0; i < n; i++) {
					double d = PointToSegmentDistance(point, ring[i], ring[(i + 1) % n]);
					if (d < best) {
						best = d;
					}
				}
			}
			return best;
		}

		static double PointToSegmentDistance(Vector2d p, Vector2d a, Vector2d b) {
			double dx = b.Lon - a.Lon;
			double dy = b.Lat - a.Lat;
			double len2 = dx * dx + dy * dy;
			if (len2 < 1e-24) {
				double ex = p.Lon - a.Lon;
				double ey = p.Lat - a.Lat;
				return Math.Sqrt(ex * ex + ey * ey);
			}
			double t = ((p.Lon - a.Lon) * dx + (p.Lat - a.Lat) * dy) / len2;
			if (t < 0.0) {
				t = 0.0;
			} else if (t > 1.0) {
				t = 1.0;
			}
			double cx = a.Lon + t * dx;
			double cy = a.Lat + t * dy;
			double ox = p.Lon - cx;
			double oy = p.Lat - cy;
			return Math.Sqrt(ox * ox + oy * oy);
		}

		static (long, long, long, long) QuantizedEdgeKey(Vector2d a, Vector2d b, double epsilon) {
			long ax = (long)Math.Round(a.Lon / epsilon);
			long ay = (long)Math.Round(a.Lat / epsilon);
			long bx = (long)Math.Round(b.Lon / epsilon);
			long by = (long)Math.Round(b.Lat / epsilon);
			if (ax > bx || (ax == bx && ay > by)) {
				return (bx, by, ax, ay);
			}
			return (ax, ay, bx, by);
		}
	}
}
