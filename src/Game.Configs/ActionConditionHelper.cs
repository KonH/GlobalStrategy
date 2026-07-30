namespace GS.Game.Configs {
	public static class ActionConditionHelper {
		public static bool TryExtractConditionThreshold(ActionDefinition def, string fieldType, out int threshold) {
			foreach (var cond in def.Conditions) {
				if (cond.Type == "gte" && cond.Members != null && cond.Members.Count >= 2) {
					if (cond.Members[0].Type == fieldType && cond.Members[1].Type == "value") {
						threshold = (int)cond.Members[1].Value;
						return true;
					}
				}
			}
			threshold = 0;
			return false;
		}
	}
}
