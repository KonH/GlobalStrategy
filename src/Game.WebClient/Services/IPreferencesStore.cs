namespace GS.Game.WebClient.Services {
	public interface IPreferencesStore {
		string? GetItem(string key);
		void SetItem(string key, string value);
		void RemoveItem(string key);
	}
}
