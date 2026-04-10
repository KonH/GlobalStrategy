using UnityEngine;

namespace GS.Unity.UI {
	[CreateAssetMenu(fileName = "LocalizationConfig", menuName = "Game/LocalizationConfig")]
	public class LocalizationConfig : ScriptableObject {
		public string DefaultLocale;
		public LocaleConfig[] Locales;
	}
}
