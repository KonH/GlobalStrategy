using System.Collections.Generic;

namespace GS.Game.Configs {
	public class MapEntryConfig {
		public List<MapFeatureEntry> Features { get; set; } = new List<MapFeatureEntry>();
	}

	public class MapFeatureEntry {
		public string GeoJsonId { get; set; } = "";
		public string NormalizedId { get; set; } = "";
		public string MapFeatureId { get; set; } = "";
	}
}
