using System.Collections.Generic;
using GS.Core.Map;
using Xunit;

namespace GS.Game.Tests {
	public class BorderSegmentIndexTests {
		[Fact]
		void shared_edge_with_reversed_winding_attributes_both_sides() {
			// Unit square A: (0,0)-(1,0)-(1,1)-(0,1); shared right edge with B.
			// B sits to the right, winding opposite on the shared edge.
			var ringsA = new[] {
				new[] {
					new Vector2d(0, 0),
					new Vector2d(1, 0),
					new Vector2d(1, 1),
					new Vector2d(0, 1)
				}
			};
			var ringsB = new[] {
				new[] {
					new Vector2d(1, 0),
					new Vector2d(2, 0),
					new Vector2d(2, 1),
					new Vector2d(1, 1)
				}
			};
			var rings = new Dictionary<string, IReadOnlyList<Vector2d[]>> {
				["A"] = ringsA,
				["B"] = ringsB
			};
			var neighbors = MutualNeighbors("A", "B");

			var map = BorderSegmentIndex.BuildNeighborMap(rings, neighbors);

			Assert.Equal("B", map["A"][0][1]); // A segment (1,0)->(1,1)
			Assert.Equal("A", map["B"][0][3]); // B segment (1,1)->(1,0)
		}

		[Fact]
		void map_edge_with_no_neighbor_stays_empty() {
			var ringsA = new[] {
				new[] {
					new Vector2d(0, 0),
					new Vector2d(1, 0),
					new Vector2d(1, 1),
					new Vector2d(0, 1)
				}
			};
			var rings = new Dictionary<string, IReadOnlyList<Vector2d[]>> {
				["A"] = ringsA
			};
			var neighbors = new Dictionary<string, IReadOnlyList<string>> {
				["A"] = new string[0]
			};

			var map = BorderSegmentIndex.BuildNeighborMap(rings, neighbors);

			Assert.Equal("", map["A"][0][0]);
			Assert.Equal("", map["A"][0][1]);
			Assert.Equal("", map["A"][0][2]);
			Assert.Equal("", map["A"][0][3]);
		}

		[Fact]
		void coordinates_within_epsilon_still_match_exact_path() {
			var ringsA = new[] {
				new[] {
					new Vector2d(0, 0),
					new Vector2d(1, 0),
					new Vector2d(1.004, 1),
					new Vector2d(0, 1)
				}
			};
			var ringsB = new[] {
				new[] {
					new Vector2d(1, 0),
					new Vector2d(2, 0),
					new Vector2d(2, 1),
					new Vector2d(0.996, 1)
				}
			};
			var rings = new Dictionary<string, IReadOnlyList<Vector2d[]>> {
				["A"] = ringsA,
				["B"] = ringsB
			};
			var neighbors = MutualNeighbors("A", "B");

			var map = BorderSegmentIndex.BuildNeighborMap(rings, neighbors, epsilon: 0.01);

			Assert.Equal("B", map["A"][0][1]);
			Assert.Equal("A", map["B"][0][3]);
		}

		[Fact]
		void coordinates_beyond_epsilon_do_not_match_exact_path_alone() {
			// Offset far enough that exact keys miss, and proximityThreshold is tiny so
			// proximity also misses — leaves the shared edge unattributed.
			var ringsA = new[] {
				new[] {
					new Vector2d(0, 0),
					new Vector2d(1, 0),
					new Vector2d(1, 1),
					new Vector2d(0, 1)
				}
			};
			var ringsB = new[] {
				new[] {
					new Vector2d(1.05, 0),
					new Vector2d(2, 0),
					new Vector2d(2, 1),
					new Vector2d(1.05, 1)
				}
			};
			var rings = new Dictionary<string, IReadOnlyList<Vector2d[]>> {
				["A"] = ringsA,
				["B"] = ringsB
			};
			var neighbors = MutualNeighbors("A", "B");

			var map = BorderSegmentIndex.BuildNeighborMap(
				rings, neighbors, epsilon: 0.01, proximityThreshold: 0.01);

			Assert.Equal("", map["A"][0][1]);
		}

		[Fact]
		void proximity_fallback_attributes_near_miss_neighbor_edges() {
			// Shared boundary endpoints offset beyond epsilon but midpoints within
			// proximityThreshold — models cross-country approximate-touch after simplify.
			var ringsA = new[] {
				new[] {
					new Vector2d(0, 0),
					new Vector2d(1, 0),
					new Vector2d(1, 1),
					new Vector2d(0, 1)
				}
			};
			var ringsB = new[] {
				new[] {
					new Vector2d(1.03, 0),
					new Vector2d(2, 0),
					new Vector2d(2, 1),
					new Vector2d(1.03, 1)
				}
			};
			var rings = new Dictionary<string, IReadOnlyList<Vector2d[]>> {
				["A"] = ringsA,
				["B"] = ringsB
			};
			var neighbors = MutualNeighbors("A", "B");

			var map = BorderSegmentIndex.BuildNeighborMap(
				rings, neighbors, epsilon: 0.01, proximityThreshold: 0.05);

			Assert.Equal("B", map["A"][0][1]);
			Assert.Equal("A", map["B"][0][3]);
		}

		static Dictionary<string, IReadOnlyList<string>> MutualNeighbors(string a, string b) {
			return new Dictionary<string, IReadOnlyList<string>> {
				[a] = new[] { b },
				[b] = new[] { a }
			};
		}
	}
}
