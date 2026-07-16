using System;
using System.Collections.Generic;

namespace GS.Game.Evals {
	public class ParameterSet {
		public int Index { get; init; }
		public IReadOnlyDictionary<string, double> Parameters { get; init; } = new Dictionary<string, double>();
	}

	public static class ParameterSearch {
		public static IReadOnlyList<ParameterSet> Generate(ParameterSearchConfig? config) {
			if (config == null || config.Parameters.Count == 0) {
				return new List<ParameterSet> { new ParameterSet { Index = 0, Parameters = new Dictionary<string, double>() } };
			}

			// Ordinal parameter-name order pins both grid enumeration order and the RNG
			// consumption order for random mode.
			var names = new List<string>(config.Parameters.Keys);
			names.Sort(StringComparer.Ordinal);
			var expanded = new Dictionary<string, List<double>>();
			foreach (var name in names) {
				expanded[name] = config.Parameters[name].Expand();
			}

			return config.Mode == "random"
				? GenerateRandom(names, expanded, config.MaxCandidates, config.SearchSeed)
				: GenerateGrid(names, expanded);
		}

		static List<ParameterSet> GenerateGrid(List<string> names, Dictionary<string, List<double>> expanded) {
			var result = new List<ParameterSet>();
			if (names.Count == 0) {
				result.Add(new ParameterSet { Index = 0, Parameters = new Dictionary<string, double>() });
				return result;
			}

			var indices = new int[names.Count];
			while (true) {
				var parameters = new Dictionary<string, double>();
				for (int i = 0; i < names.Count; i++) {
					parameters[names[i]] = expanded[names[i]][indices[i]];
				}
				result.Add(new ParameterSet { Index = result.Count, Parameters = parameters });

				int pos = names.Count - 1;
				while (pos >= 0) {
					indices[pos]++;
					if (indices[pos] < expanded[names[pos]].Count) { break; }
					indices[pos] = 0;
					pos--;
				}
				if (pos < 0) { break; }
			}
			return result;
		}

		static List<ParameterSet> GenerateRandom(List<string> names, Dictionary<string, List<double>> expanded, int maxCandidates, int searchSeed) {
			var result = new List<ParameterSet>();
			var rng = new Random(searchSeed);
			for (int i = 0; i < maxCandidates; i++) {
				var parameters = new Dictionary<string, double>();
				foreach (var name in names) {
					var values = expanded[name];
					parameters[name] = values[rng.Next(values.Count)];
				}
				result.Add(new ParameterSet { Index = i, Parameters = parameters });
			}
			return result;
		}
	}
}
