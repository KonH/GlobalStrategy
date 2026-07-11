using UnityEngine;
using GS.Core.Map;

namespace GS.Unity.Map {
	[DisallowMultipleComponent]
	public class ProvinceIdentifier : MonoBehaviour {
		internal MapFeature Feature { get; private set; }

		public string ProvinceId { get; private set; }
		public string CountryId { get; private set; }

		internal void SetProvince(string provinceId, string countryId, MapFeature feature) {
			ProvinceId = provinceId;
			CountryId = countryId;
			Feature = feature;
		}
	}
}
