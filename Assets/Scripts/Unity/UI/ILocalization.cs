namespace GS.Unity.UI {
	public interface ILocalization {
		string Get(string key);
		bool Has(string key);
		string TryGet(string key, string fallback);
		void SetLocale(string locale);
		string CurrentLocale { get; }
	}
}
