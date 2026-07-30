using System.Collections.Generic;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class ProvinceTopologyTests {
		[Fact]
		void topology_orders_ids_and_exposes_neighbors_and_distances() {
			var config = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry {
						ProvinceId = "B",
						CentroidX = 3,
						CentroidY = 4,
						NeighborProvinceIds = new List<string> { "A" }
					},
					new ProvinceEntry {
						ProvinceId = "A",
						CentroidX = 0,
						CentroidY = 0,
						NeighborProvinceIds = new List<string> { "B" }
					}
				}
			};

			var topology = new ProvinceTopology(config);

			Assert.Equal(new[] { "A", "B" }, topology.ProvinceIds);
			Assert.Equal(new[] { "B" }, topology.GetNeighbors("A"));
			Assert.Equal(25, topology.GetSquaredDistance("A", "B"));
			Assert.Empty(topology.GetNeighbors("missing"));
		}
	}
}
