using Microsoft.JSInterop;

namespace GS.Game.WebClient.Services {
	public class LocalStoragePreferencesStore : IPreferencesStore {
		readonly IJSInProcessRuntime _js;

		public LocalStoragePreferencesStore(IJSInProcessRuntime js) {
			_js = js;
		}

		public string? GetItem(string key) => _js.Invoke<string?>("localStorage.getItem", key);

		public void SetItem(string key, string value) => _js.InvokeVoid("localStorage.setItem", key, value);
	}
}
