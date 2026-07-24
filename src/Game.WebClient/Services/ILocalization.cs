namespace GS.Game.WebClient.Services {
	// Seam between the suggestion providers and however locale entries were loaded -
	// Localization is the production implementation (HttpClient fetch of wwwroot/locales/),
	// tests provide a small fake so labeled suggestions can be exercised without a browser.
	public interface ILocalization {
		string CurrentLocale { get; }
		string Get(string key);
	}
}
