using System.Collections;
using GS.Main;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.E2E {
	public static class E2ESelectRowStep {
		public static IEnumerator Execute(E2EStepContext ctx) {
			var resolved = E2EElementResolver.Resolve(ctx.Step.Name, null);
			if (!resolved.Success) {
				ctx.Fail(resolved.Error);
				yield break;
			}

			if (!(resolved.Element is ListView listView)) {
				ctx.Fail($"'{ctx.Step.Name}' is not a ListView.");
				yield break;
			}

			int index;
			if (ctx.Step.Row != null) {
				index = ctx.Step.Row.Index;
			} else {
				if (ctx.Bridge == null || ctx.Bridge.Saves == null) {
					ctx.Fail("selectRow save resolution requires SaveFileManager.");
					yield break;
				}
				var saves = ctx.Bridge.Saves.ListSaves();
				index = IndexOfSave(saves, ctx.Step.Save);
				if (index < 0) {
					var names = new System.Collections.Generic.List<string>();
					foreach (var save in saves) {
						names.Add(save.SaveName);
					}
					ctx.Fail($"Save '{ctx.Step.Save}' is not in the list. Available: {string.Join(", ", names)}");
					yield break;
				}
			}

			listView.ScrollToItem(index);
			yield return null;

			VisualElement row = listView.GetRootElementForIndex(index);
			if (row == null) {
				ctx.Fail($"ListView row {index} is not realized after ScrollToItem.");
				yield break;
			}

			var button = E2EElementResolver.FindFirstButton(row);
			if (button == null) {
				ctx.Fail($"ListView row {index} has no button.");
				yield break;
			}

			yield return E2EInputDriver.Click(button, ctx.SetInputPath, ctx.Fail);
		}

		static int IndexOfSave(System.Collections.Generic.IReadOnlyList<SaveFileInfo> saves, string saveName) {
			for (int i = 0; i < saves.Count; i++) {
				if (string.Equals(saves[i].SaveName, saveName, System.StringComparison.Ordinal)) {
					return i;
				}
			}
			return -1;
		}
	}
}
