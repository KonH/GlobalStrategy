using System.Collections.Generic;

namespace GS.Game.Evals {
	public static class SeedDerivation {
		public static IReadOnlyList<int> Seeds(int baseSeed, int count) {
			var seeds = new List<int>(count);
			for (int i = 0; i < count; i++) {
				seeds.Add(baseSeed + i);
			}
			return seeds;
		}
	}
}
