using System.Collections.Generic;
using System.Linq;
using GS.Game.E2E;
using GS.Main;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GS.Unity.E2E {
	public static class E2EStateSnapshot {
		public static CoreStateSnapshot Capture(E2ESessionBridge bridge, string inputPath, IReadOnlyList<string> extraState) {
			var snapshot = new CoreStateSnapshot {
				Scene = SceneManager.GetActiveScene().name,
				Screen = ResolveScreen(),
				InputPath = inputPath ?? ""
			};

			if (bridge != null && bridge.VisualState != null) {
				VisualState state = bridge.VisualState;
				snapshot.ActiveOrg = state.PlayerOrganization.IsValid ? state.PlayerOrganization.OrgId : "";
				snapshot.GameDate = state.Time.CurrentTime == default ? "" : state.Time.CurrentTime.ToString("yyyy-MM-dd");
				snapshot.IsPaused = state.Time.IsPaused;
				snapshot.SelectedCountry = state.SelectedCountry.IsValid ? state.SelectedCountry.CountryId : "";
				snapshot.SelectedProvince = state.SelectedProvince.IsValid ? state.SelectedProvince.ProvinceId : "";
				if (extraState != null) {
					foreach (var key in extraState) {
						string value = ProjectExtra(state, key);
						if (value != null) {
							snapshot.Extra[key] = value;
						}
					}
				}
			}

			return snapshot;
		}

		public static string ResolveScreen() {
			string scene = SceneManager.GetActiveScene().name;
			var modal = E2EElementResolver.TopmostVisibleDocument();
			if (modal != null && !string.Equals(modal.gameObject.name, scene, System.StringComparison.Ordinal)) {
				return modal.gameObject.name;
			}
			return scene;
		}

		static string ProjectExtra(VisualState state, string key) {
			switch (key) {
				case "gold":
					foreach (var resource in state.PlayerOrganization.Resources.Resources) {
						if (string.Equals(resource.ResourceId, "gold", System.StringComparison.OrdinalIgnoreCase)) {
							return resource.ActualSnapshot.ToString();
						}
					}
					return "";
				case "actionLog":
					return string.Join(" | ", state.GameLog.Entries.Select(e => e.Kind + ":" + e.OrgId));
				default:
					return null;
			}
		}
	}
}
