using System.Collections;
using GS.Game.Commands.Text;
using GS.Game.E2E;
using UnityEngine;

namespace GS.Unity.E2E {
	public static class E2ELoadSaveStep {
		public static IEnumerator EnsureSave(E2EStepContext ctx, System.Func<StepScript, IEnumerator> runScript) {
			if (ctx.Bridge == null || ctx.Bridge.Saves == null) {
				ctx.Fail("load-save setup requires SaveFileManager.");
				yield break;
			}

			if (ctx.Bridge.Saves.ListSaves().Count > 0) {
				yield break;
			}

			string path = System.IO.Path.Combine(E2EPaths.FlowsDir, "new_game_to_map.json");
			if (!System.IO.File.Exists(path)) {
				ctx.Fail("Cannot self-provision a save: new_game_to_map.json is missing.");
				yield break;
			}

			var script = StepScriptSerializer.Deserialize(System.IO.File.ReadAllText(path));
			yield return runScript(script);
			if (ctx.Failed) {
				yield break;
			}

			float bridgeDeadline = Time.realtimeSinceStartup + ctx.TimeoutSeconds;
			while (E2ERunnerHost.CurrentBridge == null && Time.realtimeSinceStartup < bridgeDeadline) {
				yield return null;
			}

			ctx.Bridge = E2ERunnerHost.CurrentBridge;
			if (ctx.Bridge == null || ctx.Bridge.Commands == null || ctx.Bridge.Saves == null) {
				ctx.Fail("load-save setup lost the session bridge after new_game_to_map.");
				yield break;
			}

			var executor = new CommandExecutor(new CommandRegistry());
			var result = executor.Execute("SaveGame", ctx.Bridge.Commands);
			if (!result.Success) {
				ctx.Fail($"Self-provision SaveGame failed: {result.Message}");
				yield break;
			}

			float deadline = Time.realtimeSinceStartup + ctx.TimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline && ctx.Bridge.Saves.ListSaves().Count == 0) {
				yield return null;
			}
			if (ctx.Bridge.Saves.ListSaves().Count == 0) {
				ctx.Fail("Self-provision SaveGame did not produce a save.");
			}
		}
	}
}
