using System.Collections;
using GS.Game.Commands;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.E2E {
	public static class E2ESelectOrgStep {
		public static IEnumerator Execute(E2EStepContext ctx) {
			string orgId = ctx.Step.Org;
			var entry = E2EOrgCatalog.Find(orgId);
			if (entry == null) {
				ctx.Fail($"Requested org '{orgId}' is not offered. Available: {string.Join(", ", E2EOrgCatalog.Ids())}");
				yield break;
			}

			if (ctx.Bridge == null || ctx.Bridge.Commands == null) {
				ctx.Fail("selectOrg requires a session bridge with a command accessor.");
				yield break;
			}

			ctx.Bridge.Commands.Push(new SelectCountryCommand(entry.HqCountryId));

			float deadline = Time.realtimeSinceStartup + ctx.TimeoutSeconds;
			while (Time.realtimeSinceStartup < deadline) {
				if (ctx.Bridge.VisualState != null
					&& ctx.Bridge.VisualState.SelectedOrganization.IsValid
					&& string.Equals(ctx.Bridge.VisualState.SelectedOrganization.OrgId, orgId, System.StringComparison.Ordinal)) {
					var start = E2EElementResolver.Resolve("btn-start", null);
					if (start.Success && start.Element.enabledSelf) {
						yield break;
					}
				}
				yield return null;
			}

			ctx.Fail($"Timed out waiting for org '{orgId}' to become selected and btn-start to enable.");
		}
	}
}
