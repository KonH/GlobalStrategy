using System.Collections.Generic;
using System.IO;
using GS.Configs;
using GS.Core.Map;

namespace GS.Game.ConsoleRunner {
	public class MapGeometryFileConfig : IConfigSource<List<MapFeature>> {
		readonly string _filePath;

		public MapGeometryFileConfig(string filePath) {
			_filePath = filePath;
		}

		public List<MapFeature> Load() {
			string json = File.ReadAllText(_filePath);
			return GeoJsonParser.Parse(json);
		}
	}
}
