using UnityEditor;
using UnityEngine;
using GS.Unity.EcsViewer;

namespace GS.Editor.EcsViewer {
	static class EcsViewerMenu {
		[MenuItem("Game/ECS Viewer/Open")]
		static void Open() {
			string? url = EcsViewerBridge.CurrentUrl;
			if (url == null) {
				Debug.LogWarning("[ECS Viewer] Server is not running — enter Play mode first.");
				return;
			}
			Application.OpenURL(url);
		}

		[MenuItem("Game/ECS Viewer/Open", validate = true)]
		static bool OpenValidate() => EcsViewerBridge.CurrentUrl != null;
	}
}
