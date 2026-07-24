namespace GS.Game.WebClient.Terminal.Suggestions {
	// One candidate for Tab completion: Value is what gets inserted into the input (the raw
	// id, command name, argument name, or OneOf literal), Label is what the UI shows (e.g.
	// "CountryName1 (CountryId1)" for labeled config-driven values, otherwise same as Value).
	public sealed record SuggestionItem(string Value, string Label);
}
