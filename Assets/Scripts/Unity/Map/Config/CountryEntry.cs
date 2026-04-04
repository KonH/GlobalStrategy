using System;
using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Map {
	[Serializable]
	public class CountryEntry {
		public string countryId;
		public string displayName;
		public List<string> mainMapFeatureIds = new List<string>();
		public List<string> secondaryMapFeatureIds = new List<string>();
		public Color color;
	}
}
