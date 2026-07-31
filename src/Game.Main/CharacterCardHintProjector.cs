using System.Collections.Generic;
using System.Linq;
using GS.Game.Configs;

namespace GS.Main {
	public static class CharacterCardHintProjector {
		public static List<CharacterCardHintRowState> Build(ActionConfig actionConfig, string roleId) {
			var rows = new List<CharacterCardHintRowState>();
			foreach (var def in actionConfig.Actions) {
				if (def.TargetRole != roleId) {
					continue;
				}
				if (!ActionConditionHelper.TryExtractConditionThreshold(def, "opinion", out int threshold)) {
					continue;
				}
				rows.Add(new CharacterCardHintRowState(def.ActionId, threshold));
			}
			return rows.OrderBy(r => r.Threshold).ToList();
		}
	}
}
