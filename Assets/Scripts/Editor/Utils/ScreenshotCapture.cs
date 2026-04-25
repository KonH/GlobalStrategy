using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GS.Editor.Utils {
	public static class ScreenshotCapture {
		const string OutputFolder = "Screenshots";

		[MenuItem("Game/Screenshot %#s")]
		static void TakeScreenshot() {
			string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputFolder));
			Directory.CreateDirectory(folder);

			string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			string path = Path.Combine(folder, $"screenshot_{timestamp}.png");

			ScreenCapture.CaptureScreenshot(path);
			Debug.Log($"[Screenshot] Saved to: {path}");
		}

		[MenuItem("Game/Screenshot %#s", validate = true)]
		static bool TakeScreenshotValidate() => EditorApplication.isPlaying;
	}
}
