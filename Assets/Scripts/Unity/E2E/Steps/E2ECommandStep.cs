using System.Collections;
using GS.Game.Commands.Text;

namespace GS.Unity.E2E {
	public static class E2ECommandStep {
		static readonly CommandExecutor Executor = new CommandExecutor(new CommandRegistry());

		public static IEnumerator Execute(E2EStepContext ctx) {
			if (ctx.Bridge == null || ctx.Bridge.Commands == null) {
				ctx.Fail("command step requires a session bridge with a command accessor.");
				yield break;
			}

			var result = Executor.Execute(ctx.Step.Line, ctx.Bridge.Commands);
			if (!result.Success) {
				ctx.Fail(result.Message);
			}
			yield break;
		}
	}
}
