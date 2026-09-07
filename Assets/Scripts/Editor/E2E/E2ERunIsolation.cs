using System.IO;
using GS.Game.E2E;
using GS.Unity.E2E;
using GS.Unity.UI;
using UnityEditor;
using UnityEngine;

namespace GS.Editor.E2E {
	public static class E2ERunIsolation {
		public static void Prepare(string runId, RunRequest request) {
			string persistent = E2EPaths.PersistentDir(runId);
			Directory.CreateDirectory(Path.Combine(persistent, "Saves"));

			bool tutorialsEnabled = request.Settings?.TutorialsEnabled ?? false;
			string locale = FindDefaultLocale();
			var settings = new {
				locale,
				tutorialsEnabled,
				completedTutorialIds = System.Array.Empty<string>()
			};
			File.WriteAllText(E2EPaths.SettingsJson(runId), E2EJson.Serialize(settings));
		}

		public static void DeletePersistent(string runId) {
			string persistent = E2EPaths.PersistentDir(runId);
			if (Directory.Exists(persistent)) {
				Directory.Delete(persistent, recursive: true);
			}
		}

		static string FindDefaultLocale() {
			string[] guids = AssetDatabase.FindAssets("t:LocalizationConfig");
			foreach (var guid in guids) {
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(path);
				if (config != null && !string.IsNullOrEmpty(config.DefaultLocale)) {
					return config.DefaultLocale;
				}
			}
			return "en";
		}
	}
}
