using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Main {
	public static class WinConditionHintProjector {
		public static (bool isAvailable, bool isAlternativeGroup, List<WinConditionHintRowState> rows) Build(
			CompletionConditionConfig? condition, int availableCountryCount) {
			var rows = new List<WinConditionHintRowState>();
			Flatten(condition, availableCountryCount, rows);
			bool isAvailable = rows.Count > 0;
			bool isAlternativeGroup = rows.Count >= 2;
			return (isAvailable, isAlternativeGroup, rows);
		}

		static void Flatten(CompletionConditionConfig? condition, int availableCountryCount, List<WinConditionHintRowState> rows) {
			if (condition == null) {
				return;
			}
			if (!CompletionConditionTypeParser.TryParse(condition.Type, out var type)) {
				return;
			}

			if (type == CompletionConditionType.Any) {
				foreach (var member in condition.Members ?? new List<CompletionConditionConfig>()) {
					Flatten(member, availableCountryCount, rows);
				}
				return;
			}
			switch (type) {
				case CompletionConditionType.TotalControl:
					rows.Add(new WinConditionHintRowState(WinConditionHintKind.TotalControl, condition.Value, availableCountryCount));
					break;
				case CompletionConditionType.FullControlCountries:
					rows.Add(new WinConditionHintRowState(WinConditionHintKind.FullControlCountries, condition.Value, availableCountryCount));
					break;
			}
		}
	}
}
