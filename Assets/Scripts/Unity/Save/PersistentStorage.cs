using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GS.Main;
using UnityEngine;

namespace GS.Unity.Save {
	public class PersistentStorage : IPersistentStorage {
		readonly string _root = Application.persistentDataPath;

#if UNITY_WEBGL && !UNITY_EDITOR
		// See Assets/Plugins/WebGL/PersistentStorageSync.jslib and .claude/rules/unity/webgl.md —
		// autoSyncPersistentDataPath alone has proven unreliable, so every write/delete also
		// triggers an explicit FS.syncfs flush to IndexedDB.
		[DllImport("__Internal")]
		static extern void PersistentStorageSyncFs();
#endif

		static void SyncFs() {
#if UNITY_WEBGL && !UNITY_EDITOR
			PersistentStorageSyncFs();
#endif
		}

		string FullPath(string relativePath) => Path.Combine(_root, relativePath);

		public void Write(string relativePath, string content) {
			string full = FullPath(relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(full)!);
			File.WriteAllText(full, content);
			SyncFs();
		}

		public string Read(string relativePath) => File.ReadAllText(FullPath(relativePath));

		public bool Exists(string relativePath) => File.Exists(FullPath(relativePath));

		public void Delete(string relativePath) {
			File.Delete(FullPath(relativePath));
			SyncFs();
		}

		public IReadOnlyList<string> List(string relativeDir) {
			string full = FullPath(relativeDir);
			if (!Directory.Exists(full)) {
				return new List<string>();
			}
			var files = Directory.GetFiles(full);
			var result = new List<string>(files.Length);
			foreach (var f in files) {
				result.Add(Path.GetFileName(f));
			}
			return result;
		}
	}
}
