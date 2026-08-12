#nullable enable
using System;
using System.Collections.Generic;

namespace GS.Unity.Common {
	public class TutorialPresentationTriggers {
		public const string MapPositionChanged = "mapPositionChanged";
		public const string MapZoomChanged = "mapZoomChanged";
		public const string KeyPressed = "keyPressed";

		public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);

		public void Set(string id, double value) {
			if (string.IsNullOrEmpty(id)) {
				return;
			}
			Values[id] = value;
		}

		public void ClearEdge(string id) {
			if (string.IsNullOrEmpty(id)) {
				return;
			}
			Values.Remove(id);
		}

		public void ClearTaskEdges() {
			Values.Remove(MapPositionChanged);
			Values.Remove(MapZoomChanged);
			Values.Remove(KeyPressed);
		}
	}
}
