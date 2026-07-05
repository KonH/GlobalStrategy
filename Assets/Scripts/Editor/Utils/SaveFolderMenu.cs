using System.IO;
using UnityEditor;
using UnityEngine;

namespace GS.Editor.Utils {
	public static class SaveFolderMenu {
		[MenuItem("GS/Saves/Open Save Folder")]
		static void OpenSaveFolder() {
			EditorUtility.RevealInFinder(Path.Combine(Application.persistentDataPath, "Saves"));
		}
	}
}
