namespace GS.Unity.UI {
	public interface ILocalization {
		string Get(string key);
		void SetLocale(string locale);
		string CurrentLocale { get; }
	}
}
