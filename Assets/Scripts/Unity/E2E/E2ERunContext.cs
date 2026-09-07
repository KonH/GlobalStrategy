using System;
using System.IO;
using GS.Game.E2E;
using UnityEngine;

namespace GS.Unity.E2E {
	public static class E2ERunContext {
		static readonly string _isolatedRoot;

		static E2ERunContext() {
			_isolatedRoot = TryReadIsolatedRoot();
		}

		public static bool IsActive => !string.IsNullOrEmpty(_isolatedRoot);

		public static string StorageRootOrDefault() {
			return string.IsNullOrEmpty(_isolatedRoot) ? Application.persistentDataPath : _isolatedRoot;
		}

		static string TryReadIsolatedRoot() {
			string lockPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".e2e", "current_run.json"));
			if (!File.Exists(lockPath)) {
				return null;
			}

			try {
				var lockFile = E2EJson.Deserialize<CurrentRunLock>(File.ReadAllText(lockPath));
				if (!string.IsNullOrEmpty(lockFile.RunFolder)) {
					return Path.Combine(lockFile.RunFolder, "persistent");
				}
				if (!string.IsNullOrEmpty(lockFile.RunId)) {
					return Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".e2e", "runs", lockFile.RunId, "persistent"));
				}
			} catch (Exception e) {
				Debug.LogError($"[E2E] Failed to read current_run.json: {e.Message}");
			}

			return null;
		}
	}
}
