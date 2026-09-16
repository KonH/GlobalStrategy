using System.IO;

namespace ECS.Viewer.Host {
	public static class WebDebugUiRoot {
		public static string FromPublishDirectory(string publishDir) {
			if (string.IsNullOrWhiteSpace(publishDir)) {
				return "";
			}

			string full = Path.GetFullPath(publishDir);
			string nested = Path.Combine(full, "wwwroot");
			if (LooksPublished(nested)) {
				return nested;
			}

			return full;
		}

		public static bool LooksPublished(string staticRoot) {
			if (string.IsNullOrWhiteSpace(staticRoot)) {
				return false;
			}

			return File.Exists(Path.Combine(staticRoot, "index.html"))
				&& Directory.Exists(Path.Combine(staticRoot, "_framework"));
		}
	}
}
