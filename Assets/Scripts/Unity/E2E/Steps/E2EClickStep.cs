using System.Collections;

namespace GS.Unity.E2E {
	public static class E2EClickStep {
		public static IEnumerator Execute(E2EStepContext ctx) {
			var resolved = E2EElementResolver.Resolve(ctx.Step.Name, ctx.Step.Label);
			if (!resolved.Success) {
				ctx.Fail(resolved.Error);
				yield break;
			}
			yield return E2EInputDriver.Click(resolved.Element, ctx.SetInputPath, ctx.Fail);
		}
	}
}
