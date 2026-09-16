namespace GS.Game.Commands.Text.Suggestions {
	public interface ISuggestionLabels {
		string Country(string id, string displayName);
		string Org(string id, string displayName);
		string Province(string id, string displayName);
		string Action(string id, string nameKey);
		string Role(string id, string nameKey);
		string Raw(string id);
	}
}
