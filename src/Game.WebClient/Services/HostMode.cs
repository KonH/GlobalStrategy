using System;

namespace GS.Game.WebClient.Services {
	public static class HostMode {
		public static bool IsRemote(string uri) {
			if (string.IsNullOrEmpty(uri)) {
				return false;
			}
			if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)) {
				int q = uri.IndexOf('?');
				string query = q >= 0 ? uri.Substring(q + 1) : "";
				return QueryHasRemote(query);
			}
			return QueryHasRemote(parsed.Query.TrimStart('?'));
		}

		public static bool ShouldRedirectToMenu(string uri, bool hasInProcessLogic) {
			if (IsRemote(uri)) {
				return false;
			}
			return !hasInProcessLogic;
		}

		static bool QueryHasRemote(string query) {
			foreach (string part in query.Split('&')) {
				int eq = part.IndexOf('=');
				string name = eq >= 0 ? Uri.UnescapeDataString(part.Substring(0, eq)) : Uri.UnescapeDataString(part);
				string value = eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1)) : "";
				if (string.Equals(name, "host", StringComparison.OrdinalIgnoreCase)
					&& string.Equals(value, "remote", StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
			}
			return false;
		}
	}
}
