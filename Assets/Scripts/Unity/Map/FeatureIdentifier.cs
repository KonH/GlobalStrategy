using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	[DisallowMultipleComponent]
	public class FeatureIdentifier : MonoBehaviour {
		internal MapFeature Feature { get; private set; }

		public string FeatureName { get; private set; }
		public string FeatureId { get; private set; }

		internal void SetFeature(MapFeature feature) {
			Feature = feature;
			FeatureName = feature.Name;
			FeatureId = feature.Id;
		}
	}
}
