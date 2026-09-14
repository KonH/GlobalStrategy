using System.Collections;
using GS.Unity.Map;
using UnityEngine;

namespace GS.Unity.E2E {
	public static class E2ESelectCountryStep {
		public static IEnumerator Execute(E2EStepContext ctx) {
			string countryId = ctx.Step.Country;
			if (string.IsNullOrEmpty(countryId)) {
				ctx.Fail("selectCountry step is missing a country target.");
				yield break;
			}

			var cameras = Object.FindObjectsByType<MapCameraController>(FindObjectsInactive.Exclude);
			MapCameraController camera = null;
			for (int i = 0; i < cameras.Length; i++) {
				if (cameras[i] != null && cameras[i].isActiveAndEnabled) {
					camera = cameras[i];
					break;
				}
			}
			if (camera == null) {
				ctx.Fail("selectCountry could not find an active MapCameraController.");
				yield break;
			}

			if (!camera.TryPrepareCountryClick(countryId, out var screenPoint)) {
				ctx.Fail($"selectCountry could not prepare a map click for '{countryId}'.");
				yield break;
			}

			Debug.Log($"[E2E] selectCountry '{countryId}' device click at {screenPoint}");
			yield return null;
			yield return E2EInputDriver.ClickScreen(screenPoint, ctx.SetInputPath, ctx.Fail);
			if (ctx.Failed) {
				yield break;
			}

			float deadline = Time.realtimeSinceStartup + ctx.TimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline) {
				if (ctx.Bridge != null
					&& ctx.Bridge.VisualState != null
					&& ctx.Bridge.VisualState.SelectedCountry.IsValid
					&& string.Equals(ctx.Bridge.VisualState.SelectedCountry.CountryId, countryId, System.StringComparison.Ordinal)) {
					yield break;
				}
				yield return null;
			}

			string actual = ctx.Bridge?.VisualState != null && ctx.Bridge.VisualState.SelectedCountry.IsValid
				? ctx.Bridge.VisualState.SelectedCountry.CountryId
				: "(none)";
			ctx.Fail($"Timed out waiting for country '{countryId}' to become selected (selected: '{actual}').");
		}
	}
}
