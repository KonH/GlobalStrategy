namespace GS.Game.Configs {
	public enum CompletionConditionType { Any, TotalControl, FullControlCountries }

	public static class CompletionConditionTypeParser {
		public static bool TryParse(string raw, out CompletionConditionType type) {
			switch (raw) {
				case "any":
					type = CompletionConditionType.Any;
					return true;
				case "total_control":
					type = CompletionConditionType.TotalControl;
					return true;
				case "full_control_countries":
					type = CompletionConditionType.FullControlCountries;
					return true;
				default:
					type = default;
					return false;
			}
		}
	}
}
