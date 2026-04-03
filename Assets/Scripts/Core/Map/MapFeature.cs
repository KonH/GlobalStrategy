using System.Collections.Generic;

namespace GS.Core.Map {
	public class MapFeature {
		public string Id;
		public string Name;
		public List<Polygon> Polygons;

		public MapFeature(string id, string name, List<Polygon> polygons) {
			Id = id;
			Name = name;
			Polygons = polygons;
		}
	}
}
