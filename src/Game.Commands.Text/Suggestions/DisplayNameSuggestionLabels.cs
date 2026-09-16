namespace GS.Game.Commands.Text.Suggestions {
	public sealed class DisplayNameSuggestionLabels : ISuggestionLabels {
		public string Country(string id, string displayName) {
			return FirstNonEmpty(displayName, id);
		}

		public string Org(string id, string displayName) {
			return FirstNonEmpty(displayName, id);
		}

		public string Province(string id, string displayName) {
			return FirstNonEmpty(displayName, id);
		}

		public string Action(string id, string nameKey) {
			return FirstNonEmpty(nameKey, id);
		}

		public string Role(string id, string nameKey) {
			return FirstNonEmpty(nameKey, id);
		}

		public string Raw(string id) {
			return id;
		}

		static string FirstNonEmpty(string preferred, string fallback) {
			return string.IsNullOrEmpty(preferred) ? fallback : preferred;
		}
	}
}
