using System.Collections.Generic;

namespace GS.Main {
	public static class DictionaryExtensions {
		public static AnimatableInt GetOrCreate(this Dictionary<string, AnimatableInt> dict, string key) {
			if (!dict.TryGetValue(key, out var v)) {
				v = new AnimatableInt();
				dict[key] = v;
			}
			return v;
		}
	}
}
