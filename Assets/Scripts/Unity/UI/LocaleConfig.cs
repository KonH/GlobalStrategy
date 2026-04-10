using System;
using UnityEngine;

namespace GS.Unity.UI {
	[Serializable]
	public class LocaleEntry {
		public string Key;
		public string Value;
	}

	[CreateAssetMenu(fileName = "LocaleConfig", menuName = "Game/LocaleConfig")]
	public class LocaleConfig : ScriptableObject {
		public string Locale;
		public LocaleEntry[] Entries;
	}
}
