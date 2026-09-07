using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace GS.Unity.Common {
	public static class ScreenCaptureUtil {
		public static IEnumerator CaptureTo(string path) {
			yield return new WaitForEndOfFrame();
			CaptureImmediate(path);
		}

		public static void CaptureImmediate(string path) {
			var texture = ScreenCapture.CaptureScreenshotAsTexture();
			if (texture == null) {
				throw new InvalidOperationException($"Screen capture produced no texture for '{path}'.");
			}
			try {
				string directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory)) {
					Directory.CreateDirectory(directory);
				}
				File.WriteAllBytes(path, texture.EncodeToPNG());
			} finally {
				UnityEngine.Object.Destroy(texture);
			}
		}
	}
}
