using System.Collections.Generic;

namespace GS.Core.Map {
	// index 0 = outer ring, 1+ = holes
	public class Polygon {
		public List<Ring> Rings;

		public Polygon(List<Ring> rings) {
			Rings = rings;
		}
	}
}
