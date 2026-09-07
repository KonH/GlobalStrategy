using System.Collections;
using UnityEngine.UIElements;

namespace GS.Unity.E2E {
	public static class E2EClickStep {
		public static IEnumerator Execute(E2EStepContext ctx) {
			var resolved = E2EElementResolver.Resolve(ctx.Step.Name, ctx.Step.Label);
			if (!resolved.Success) {
				ctx.Fail(resolved.Error);
				yield break;
			}
			VisualElement target = resolved.Element;
			while (target != null && target.pickingMode == PickingMode.Ignore) {
				target = target.parent;
			}
			if (target == null) {
				ctx.Fail("click target and its ancestors have picking disabled.");
				yield break;
			}
			yield return E2EInputDriver.Click(target, ctx.SetInputPath, ctx.Fail);
		}
	}
}
