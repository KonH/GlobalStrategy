using UnityEngine;

namespace GS.Unity.Common {
	// Unity has no serialized "max FPS" field in Project Settings - Application.targetFrameRate
	// is the only mechanism, and it's ignored while QualitySettings.vSyncCount is non-zero.
	// Runs once before the first scene loads so it applies regardless of which scene starts
	// the player (MainMenu, SelectCountry, Game).
	static class FrameRateSettings {
		const int MaxFrameRate = 60;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		static void Apply() {
			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = MaxFrameRate;
		}
	}
}
