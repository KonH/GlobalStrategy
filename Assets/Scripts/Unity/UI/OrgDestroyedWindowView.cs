using System;
using GS.Main;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	class OrgDestroyedWindowView {
		readonly ILocalization _loc;
		readonly Label _title;
		readonly Label _body;
		readonly Button _confirm;
		Func<string, string, string> _getText = (_, fallback) => fallback;

		public OrgDestroyedWindowView(VisualElement root, ILocalization loc) {
			_loc = loc;
			_title = root.Q<Label>("org-destroyed-title");
			_body = root.Q<Label>("org-destroyed-body");
			_confirm = root.Q<Button>("btn-confirm");
		}

		public void RefreshStaticTexts(Func<string, string, string> getText) {
			_getText = getText ?? _getText;
			if (_confirm != null) {
				_confirm.text = _getText("org_destroyed.confirm", "Continue");
			}
		}

		public void Refresh(OrgDestroyedSnapshotState snapshot) {
			if (snapshot == null) {
				return;
			}

			string orgName = GetOrgName(snapshot.OrganizationId);
			if (_title != null) {
				_title.text = string.Format(
					GetLoc("org_destroyed.header_format", "{0} unravels in the shadows..."),
					orgName);
			}
			if (_body != null) {
				_body.text = string.Format(
					GetLoc(
						"org_destroyed.body_format",
						"Rival hands wove the threads that brought it down. {0} no longer stands among the powers of this world."),
					orgName);
			}
		}

		string GetOrgName(string orgId) {
			string key = $"organization_name.{orgId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}

			return orgId ?? "";
		}

		string GetLoc(string key, string fallback) {
			return _getText(key, fallback);
		}
	}
}
