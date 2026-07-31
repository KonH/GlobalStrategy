using System;
using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class ProvinceTopology {
		readonly Dictionary<string, string[]> _neighbors = new Dictionary<string, string[]>(StringComparer.Ordinal);
		readonly Dictionary<string, (double X, double Y)> _centroids =
			new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
		readonly Dictionary<string, bool> _isMainTerritory = new Dictionary<string, bool>(StringComparer.Ordinal);

		public string[] ProvinceIds { get; }

		public ProvinceTopology(ProvinceConfig config) {
			var ids = new List<string>();
			foreach (ProvinceEntry entry in config.Provinces) {
				if (!double.IsFinite(entry.CentroidX) || !double.IsFinite(entry.CentroidY)) {
					throw new InvalidOperationException($"Province '{entry.ProvinceId}' has a non-finite centroid.");
				}
				if (_centroids.ContainsKey(entry.ProvinceId)) {
					throw new InvalidOperationException($"Province topology contains duplicate id '{entry.ProvinceId}'.");
				}
				ids.Add(entry.ProvinceId);
				_centroids[entry.ProvinceId] = (entry.CentroidX, entry.CentroidY);
				_isMainTerritory[entry.ProvinceId] = entry.IsMainTerritory;
				var neighbors = entry.NeighborProvinceIds?.ToArray() ?? Array.Empty<string>();
				Array.Sort(neighbors, StringComparer.Ordinal);
				_neighbors[entry.ProvinceId] = neighbors;
			}
			ids.Sort(StringComparer.Ordinal);
			ProvinceIds = ids.ToArray();
			foreach (string provinceId in ProvinceIds) {
				var seen = new HashSet<string>(StringComparer.Ordinal);
				foreach (string neighborId in _neighbors[provinceId]) {
					if (neighborId == provinceId) {
						throw new InvalidOperationException($"Province '{provinceId}' cannot neighbor itself.");
					}
					if (!_centroids.ContainsKey(neighborId)) {
						throw new InvalidOperationException(
							$"Province '{provinceId}' references unknown neighbor '{neighborId}'.");
					}
					if (!seen.Add(neighborId)) {
						throw new InvalidOperationException(
							$"Province '{provinceId}' contains duplicate neighbor '{neighborId}'.");
					}
				}
			}
			foreach (string provinceId in ProvinceIds) {
				foreach (string neighborId in _neighbors[provinceId]) {
					if (Array.BinarySearch(
						_neighbors[neighborId], provinceId, StringComparer.Ordinal) < 0) {
						throw new InvalidOperationException(
							$"Province adjacency is not symmetric between '{provinceId}' and '{neighborId}'.");
					}
				}
			}
		}

		public IReadOnlyList<string> GetNeighbors(string provinceId) {
			return _neighbors.TryGetValue(provinceId, out string[]? neighbors)
				? neighbors
				: Array.Empty<string>();
		}

		public bool IsMainTerritory(string provinceId) {
			return !_isMainTerritory.TryGetValue(provinceId, out bool isMain) || isMain;
		}

		public double GetSquaredDistance(string provinceA, string provinceB) {
			if (!_centroids.TryGetValue(provinceA, out var a)) {
				throw new InvalidOperationException($"Unknown province centroid '{provinceA}'.");
			}
			if (!_centroids.TryGetValue(provinceB, out var b)) {
				throw new InvalidOperationException($"Unknown province centroid '{provinceB}'.");
			}
			double dx = a.X - b.X;
			double dy = a.Y - b.Y;
			return dx * dx + dy * dy;
		}
	}
}
