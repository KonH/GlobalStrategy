using System;
using Microsoft.AspNetCore.Components;

namespace GS.Game.WebClient.Services {
	/// <summary>
	/// Navigation helpers that keep in-app routes under a non-root <c>&lt;base href&gt;</c>
	/// (GitHub Pages project site at <c>/GlobalStrategy/</c>).
	/// </summary>
	public static class AppNavigation {
		/// <summary>
		/// Navigate to an app-relative path. A leading <c>/</c> is stripped so
		/// <see cref="Uri"/> resolution does not drop the base path (host-root absolute).
		/// </summary>
		public static void NavigateTo(NavigationManager navigation, string appRelativePath) {
			if (navigation == null) {
				throw new ArgumentNullException(nameof(navigation));
			}
			navigation.NavigateTo(ToBaseRelativeTarget(appRelativePath));
		}

		/// <summary>
		/// Strip a leading slash so the target is resolved against <see cref="NavigationManager.BaseUri"/>.
		/// </summary>
		public static string ToBaseRelativeTarget(string? appRelativePath) {
			if (string.IsNullOrEmpty(appRelativePath)) {
				return "";
			}
			return appRelativePath.TrimStart('/');
		}

		/// <summary>
		/// Path under the app base, without query/fragment, no trailing slash
		/// (empty string at the app root).
		/// </summary>
		public static string CurrentBaseRelativePath(NavigationManager navigation) {
			if (navigation == null) {
				throw new ArgumentNullException(nameof(navigation));
			}
			string relative = navigation.ToBaseRelativePath(navigation.Uri);
			int query = relative.IndexOf('?');
			if (query >= 0) {
				relative = relative.Substring(0, query);
			}
			int hash = relative.IndexOf('#');
			if (hash >= 0) {
				relative = relative.Substring(0, hash);
			}
			return relative.TrimEnd('/');
		}
	}
}
