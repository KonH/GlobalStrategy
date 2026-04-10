using UnityEditor;
using GS.Unity.UI;

namespace GS.Editor.Localization {
	static class LocaleMenu {
		const string ConfigPath = "Assets/Localization/LocalizationConfig.asset";

		[MenuItem("Game/Locale/English")]
		static void SetEnglish() => SetLocale("en");

		[MenuItem("Game/Locale/Russian")]
		static void SetRussian() => SetLocale("ru");

		static void SetLocale(string locale) {
			var config = AssetDatabase.LoadAssetAtPath<LocalizationConfig>(ConfigPath);
			if (config == null) return;
			config.DefaultLocale = locale;
			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
		}
	}
}
