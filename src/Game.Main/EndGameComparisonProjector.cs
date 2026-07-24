using System;
using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Main {
	public static class EndGameComparisonProjector {
		public static List<EndGameComparisonRowState> Build(
			IReadOnlyList<EndGameComparisonEntry> configuredEntries, string playerOrgId, string playerDisplayName, double playerScore) {
			var rows = new List<(string ComparisonElementId, bool IsPlayer, string DisplayName, double Score)>();
			if (configuredEntries != null) {
				foreach (var entry in configuredEntries) {
					rows.Add((entry.ComparisonElementId, false, entry.ComparisonElementId, entry.Score));
				}
			}
			rows.Add((playerOrgId, true, playerDisplayName, playerScore));

			// Deterministic tie-break: score desc, then ComparisonElementId ordinal, then IsPlayer
			// (false before true) so the player row lands after a same-scored/same-id config entry.
			rows.Sort((a, b) => {
				int scoreCompare = b.Score.CompareTo(a.Score);
				if (scoreCompare != 0) {
					return scoreCompare;
				}
				int idCompare = StringComparer.Ordinal.Compare(a.ComparisonElementId, b.ComparisonElementId);
				if (idCompare != 0) {
					return idCompare;
				}
				return a.IsPlayer.CompareTo(b.IsPlayer);
			});

			var result = new List<EndGameComparisonRowState>(rows.Count);
			for (int i = 0; i < rows.Count; i++) {
				var row = rows[i];
				result.Add(new EndGameComparisonRowState(i + 1, row.ComparisonElementId, row.IsPlayer, row.DisplayName, row.Score));
			}
			return result;
		}
	}
}
