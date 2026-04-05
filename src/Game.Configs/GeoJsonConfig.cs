using System.Collections.Generic;

namespace GS.Game.Configs {
	public class GeoJsonConfig {
		public List<GeoJsonFeatureConfig> Features { get; set; } = new List<GeoJsonFeatureConfig>();
	}

	public class GeoJsonFeatureConfig {
		public string Name { get; set; } = "";
		public string PartOf { get; set; } = "";
	}
}
