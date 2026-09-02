using System;
using GS.Main;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	public class CountryDestroyedWindowView {
		readonly ILocalization _loc;
		readonly Label _title;
		readonly Label _body;
		readonly Button _confirm;
		Func<string, string, string> _getText = (_, fallback) => fallback;

		public CountryDestroyedWindowView(VisualElement root, ILocalization loc) {
			_loc = loc;
			_title = root.Q<Label>("country-destroyed-title");
			_body = root.Q<Label>("country-destroyed-body");
			_confirm = root.Q<Button>("btn-confirm");
		}

		public void RefreshStaticTexts(Func<string, string, string> getText) {
			_getText = getText ?? _getText;
			if (_confirm != null) {
				_confirm.text = _getText("country_destroyed.confirm", "Continue");
			}
		}

		public void Refresh(CountryDestroyedSnapshotState snapshot) {
			if (snapshot == null) {
				return;
			}

			string countryName = GetCountryName(snapshot.CountryId);
			if (_title != null) {
				_title.text = string.Format(
					GetLoc("country_destroyed.header_format", "{0} is lost in the past..."),
					countryName);
			}
			if (_body != null) {
				_body.text = string.Format(
					GetLoc(
						"country_destroyed.body_format",
						"Its lands have fallen silent. {0} no longer appears among the nations of this world."),
					countryName);
			}
		}

		string GetCountryName(string countryId) {
			string key = $"country_name.{countryId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}
			return countryId ?? "";
		}

		string GetLoc(string key, string fallback) {
			return _getText(key, fallback);
		}
	}
}
