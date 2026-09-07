using UnityEngine;

namespace GS.Unity.E2E {
	public static class E2ERunnerBootstrap {
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Init() {
			if (!E2ERunContext.IsActive) {
				return;
			}

			var go = new GameObject("E2ERunnerHost");
			Object.DontDestroyOnLoad(go);
			go.AddComponent<E2ERunnerHost>();
		}
	}
}
