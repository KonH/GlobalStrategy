using UnityEditor;
using UnityEngine;
using VContainer;
using GS.Unity.DI;
using GS.Main;
using GS.Game.Commands;

namespace GS.Editor.Localization {
	static class LocaleMenu {
		[MenuItem("Game/Locale/English")]
		static void SetEnglish() => SetLocale("en");

		[MenuItem("Game/Locale/Russian")]
		static void SetRussian() => SetLocale("ru");

		static void SetLocale(string locale) {
			if (!Application.isPlaying) {
				Debug.LogWarning("[LocaleMenu] Locale can only be changed at runtime");
				return;
			}
			var scope = Object.FindObjectOfType<GameLifetimeScope>();
			if (scope == null) {
				Debug.LogWarning("[LocaleMenu] GameLifetimeScope not found in scene");
				return;
			}
			var commands = scope.Container.Resolve<IWriteOnlyCommandAccessor>();
			commands.Push(new ChangeLocaleCommand(locale));
		}
	}
}
