using System.Collections.Generic;
using UnityEngine;

namespace GS.Unity.Map {
	[CreateAssetMenu(fileName = "CountryVisualConfig", menuName = "GlobalStrategy/Country Visual Config")]
	public class CountryVisualConfig : ScriptableObject {
		public List<CountryVisualEntry> Entries = new List<CountryVisualEntry>();

		public CountryVisualEntry Find(string countryId) {
			foreach (var entry in Entries) {
				if (entry.countryId == countryId) {
					return entry;
				}
			}
			return null;
		}
	}
}
