using System;
using System.Globalization;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Map;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	class WarResultWindowView {
		readonly ILocalization _loc;
		readonly WarProgressLayoutBinder _binder;
		readonly Label _winner;
		readonly Label _resultsTitle;
		readonly Label _goldTitle;
		readonly Label _goldTaken;
		readonly VisualElement _goldList;
		readonly Label _controlTitle;
		readonly VisualElement _controlList;
		readonly Label _provincesTitle;
		readonly VisualElement _provincesList;
		Func<string, string, string> _getText = (_, fallback) => fallback;

		public WarResultWindowView(
			VisualElement root, ILocalization loc, CountryVisualConfig countryVisualConfig,
			EffectConfig effectConfig, TooltipSystem tooltip) {
			_loc = loc;
			_binder = new WarProgressLayoutBinder(root, loc, countryVisualConfig, effectConfig, tooltip);
			_winner = root.Q<Label>("war-result-winner");
			_resultsTitle = root.Q<Label>("war-result-results-title");
			_goldTitle = root.Q<Label>("war-result-gold-title");
			_goldTaken = root.Q<Label>("war-result-gold-taken");
			_goldList = root.Q<VisualElement>("war-result-gold-list");
			_controlTitle = root.Q<Label>("war-result-control-title");
			_controlList = root.Q<VisualElement>("war-result-control-list");
			_provincesTitle = root.Q<Label>("war-result-provinces-title");
			_provincesList = root.Q<VisualElement>("war-result-provinces-list");
		}

		public void RefreshStaticTexts(Func<string, string, string> getText) {
			_getText = getText ?? _getText;
			_binder.RefreshStaticTexts(_getText);
			if (_resultsTitle != null) {
				_resultsTitle.text = _getText("war_result.results_title", "Results");
			}
			if (_goldTitle != null) {
				_goldTitle.text = _getText("war_result.gold_title", "Gold");
			}
			if (_controlTitle != null) {
				_controlTitle.text = _getText("war_result.control_title", "Control");
			}
			if (_provincesTitle != null) {
				_provincesTitle.text = _getText("war_result.provinces_title", "Provinces");
			}
		}

		public void Refresh(WarResultSnapshotState snapshot) {
			if (snapshot == null) {
				return;
			}

			_binder.Refresh(
				snapshot.AttackerCountryId,
				snapshot.DefenderCountryId,
				snapshot.Progress,
				snapshot.History,
				snapshot.Attacker,
				snapshot.Defender,
				snapshot.Battles);

			if (_winner != null) {
				_winner.text = string.Format(
					GetLoc("war_result.winner_format", "{0} won!"),
					GetCountryName(snapshot.WinnerCountryId));
			}

			RefreshGold(snapshot);
			RefreshControl(snapshot);
			RefreshProvinces(snapshot);
		}

		void RefreshGold(WarResultSnapshotState snapshot) {
			if (_goldTaken != null) {
				if (snapshot.GoldTaken == 0 && snapshot.GoldRecipients.Count == 0) {
					_goldTaken.text = GetLoc("war_result.gold_empty", "No gold taken");
				} else {
					_goldTaken.text = string.Format(
						GetLoc("war_result.gold_taken_format", "Gold taken: {0}"),
						FormatGold(snapshot.GoldTaken));
				}
			}

			if (_goldList == null) {
				return;
			}
			_goldList.Clear();
			foreach (WarGoldRecipientState recipient in snapshot.GoldRecipients) {
				string line;
				if (recipient.OwnerType == OwnerType.Org) {
					line = string.Format(
						GetLoc("war_result.gold_recipient_org_format", "{0}: {1}"),
						GetOrgName(recipient.OwnerId),
						FormatGold(recipient.Amount));
				} else {
					line = string.Format(
						GetLoc("war_result.gold_recipient_country_format", "{0}: {1}"),
						GetCountryName(recipient.OwnerId),
						FormatGold(recipient.Amount));
				}
				_goldList.Add(CreateRow(line));
			}
		}

		void RefreshControl(WarResultSnapshotState snapshot) {
			if (_controlList == null) {
				return;
			}
			_controlList.Clear();
			if (snapshot.ControlDeltas.Count == 0) {
				_controlList.Add(CreateRow(GetLoc("war_result.control_empty", "No control changes")));
				return;
			}
			foreach (WarControlDeltaState delta in snapshot.ControlDeltas) {
				string signedDelta = delta.Delta > 0
					? $"+{delta.Delta.ToString(CultureInfo.InvariantCulture)}"
					: delta.Delta.ToString(CultureInfo.InvariantCulture);
				string line = string.Format(
					GetLoc("war_result.control_delta_format", "{0} in {1}: {2} (now {3})"),
					GetOrgName(delta.OrgId),
					GetCountryName(delta.CountryId),
					signedDelta,
					delta.TotalAfter.ToString(CultureInfo.InvariantCulture));
				_controlList.Add(CreateRow(line));
			}
		}

		void RefreshProvinces(WarResultSnapshotState snapshot) {
			if (_provincesList == null) {
				return;
			}
			_provincesList.Clear();
			if (snapshot.TransferredProvinceIds.Count == 0) {
				_provincesList.Add(CreateRow(GetLoc("war_result.provinces_empty", "No provinces transferred")));
				return;
			}
			foreach (string provinceId in snapshot.TransferredProvinceIds) {
				_provincesList.Add(CreateRow(GetProvinceName(provinceId)));
			}
		}

		static Label CreateRow(string text) {
			var row = new Label(text);
			row.AddToClassList("war-result-row");
			return row;
		}

		string GetCountryName(string countryId) {
			string key = $"country_name.{countryId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}
			return countryId ?? "";
		}

		string GetOrgName(string orgId) {
			string key = $"organization_name.{orgId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}
			return orgId ?? "";
		}

		string GetProvinceName(string provinceId) {
			string key = $"province_name.{provinceId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}
			return provinceId ?? "";
		}

		string GetLoc(string key, string fallback) {
			return _getText(key, fallback);
		}

		static string FormatGold(double value) {
			return value.ToString("0.#", CultureInfo.InvariantCulture);
		}
	}
}
