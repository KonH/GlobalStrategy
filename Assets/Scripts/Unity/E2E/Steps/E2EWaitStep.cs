using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GS.Unity.E2E {
	public static class E2EWaitStep {
		public static IEnumerator Execute(E2EStepContext ctx) {
			float deadline = Time.realtimeSinceStartup + ctx.TimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline) {
				if (ConditionHolds(ctx)) {
					yield break;
				}
				yield return null;
			}
			ctx.Fail($"Timed out waiting for {Describe(ctx)}.");
		}

		static bool ConditionHolds(E2EStepContext ctx) {
			var step = ctx.Step;
			if (!string.IsNullOrEmpty(step.Screen)) {
				string scene = SceneManager.GetActiveScene().name;
				string screen = E2EStateSnapshot.ResolveScreen();
				if (!string.Equals(scene, step.Screen, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(screen, step.Screen, StringComparison.OrdinalIgnoreCase)) {
					return false;
				}
			}
			if (!string.IsNullOrEmpty(step.Control)) {
				var resolved = E2EElementResolver.Resolve(step.Control, null);
				if (!resolved.Success) {
					return false;
				}
			}
			if (!string.IsNullOrEmpty(step.GameDate) && ctx.Bridge != null && ctx.Bridge.VisualState != null) {
				string current = ctx.Bridge.VisualState.Time.CurrentTime.ToString("yyyy-MM-dd");
				if (!string.Equals(current, step.GameDate, StringComparison.Ordinal)) {
					return false;
				}
			}
			if (step.Pause != null && ctx.Bridge != null && ctx.Bridge.VisualState != null) {
				if (ctx.Bridge.VisualState.Time.IsPaused != step.Pause.Value) {
					return false;
				}
			}
			return true;
		}

		static string Describe(E2EStepContext ctx) {
			var step = ctx.Step;
			if (!string.IsNullOrEmpty(step.Screen)) {
				return $"screen '{step.Screen}'";
			}
			if (!string.IsNullOrEmpty(step.Control)) {
				return $"control '{step.Control}'";
			}
			if (!string.IsNullOrEmpty(step.GameDate)) {
				return $"gameDate '{step.GameDate}'";
			}
			if (step.Pause != null) {
				return $"pause={step.Pause.Value}";
			}
			return "waitFor condition";
		}
	}
}
