using System;
using System.Collections.Generic;
using System.Globalization;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Map;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	public class WarResultWindowView {
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
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
			_countryVisualConfig = countryVisualConfig;
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
				// Title text is rebuilt with the coin icon in RefreshGold.
				_goldTitle.style.display = DisplayStyle.None;
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
			IReadOnlyList<WarGoldRecipientState> recipients = snapshot.GoldRecipients
				?? Array.Empty<WarGoldRecipientState>();
			if (_goldTaken != null) {
				_goldTaken.style.display = DisplayStyle.None;
			}
			if (_goldList == null) {
				return;
			}
			_goldList.Clear();
			_goldList.Add(CreateGoldHeaderRow());

			if (snapshot.GoldTaken == 0 && recipients.Count == 0) {
				_goldList.Add(CreateTextRow(GetLoc("war_result.gold_empty", "No gold taken")));
				return;
			}

			var orgRecipients = new List<WarGoldRecipientState>();
			double orgTotal = 0;
			foreach (WarGoldRecipientState recipient in recipients) {
				if (recipient.OwnerType == OwnerType.Org && recipient.Amount != 0) {
					orgRecipients.Add(recipient);
					orgTotal += recipient.Amount;
				}
			}

			string amount = FormatGold(snapshot.GoldTaken);
			_goldList.Add(CreateTextRow(
				string.Format(GetLoc("war_result.gold_country_header_format", "{0}:"),
					GetCountryName(snapshot.LoserCountryId)),
				"war-result-gold-country"));
			_goldList.Add(CreateTextRow(
				string.Format(GetLoc("war_result.gold_amount_lost_format", "-{0}"), amount),
				"war-result-gold-amount"));

			_goldList.Add(CreateTextRow(
				string.Format(GetLoc("war_result.gold_country_header_format", "{0}:"),
					GetCountryName(snapshot.WinnerCountryId)),
				"war-result-gold-country"));
			_goldList.Add(CreateTextRow(
				string.Format(GetLoc("war_result.gold_amount_gained_format", "+{0}"), amount),
				"war-result-gold-amount"));

			if (orgTotal > 0) {
				_goldList.Add(CreateTextRow(
					string.Format(
						GetLoc("war_result.gold_orgs_deduction_format", "-{0} for organizations:"),
						FormatGold(orgTotal)),
					"war-result-gold-amount"));
			}

			foreach (WarGoldRecipientState recipient in orgRecipients) {
				int percent = snapshot.GoldTaken > 0
					? (int)Math.Round(recipient.Amount * 100.0 / snapshot.GoldTaken, MidpointRounding.AwayFromZero)
					: 0;
				_goldList.Add(CreateTextRow(
					string.Format(
						GetLoc("war_result.gold_org_format", "{0} ({1}%): +{2}"),
						GetOrgName(recipient.OwnerId),
						percent.ToString(CultureInfo.InvariantCulture),
						FormatGold(recipient.Amount)),
					"war-result-gold-amount"));
			}
		}

		VisualElement CreateGoldHeaderRow() {
			var row = new VisualElement();
			row.AddToClassList("war-result-gold-header");

			var icon = new VisualElement();
			icon.AddToClassList("resource-chip-icon");
			icon.AddToClassList("resource-icon--coin");
			icon.AddToClassList("war-result-gold-icon");

			var label = new Label(GetLoc("war_result.gold_title", "Gold"));
			label.AddToClassList("gs-label");
			label.AddToClassList("war-result-subsection-title");
			label.AddToClassList("war-result-gold-header-label");

			row.Add(icon);
			row.Add(label);
			return row;
		}

		static Label CreateTextRow(string text, string extraClass = null) {
			var row = new Label(text);
			row.AddToClassList("gs-content");
			row.AddToClassList("war-result-row");
			if (!string.IsNullOrEmpty(extraClass)) {
				row.AddToClassList(extraClass);
			}
			return row;
		}

		void RefreshControl(WarResultSnapshotState snapshot) {
			if (_controlList == null) {
				return;
			}
			_controlList.Clear();
			IReadOnlyList<WarControlDeltaState> deltas = snapshot.ControlDeltas
				?? Array.Empty<WarControlDeltaState>();
			if (deltas.Count == 0) {
				_controlList.Add(CreateRow(GetLoc("war_result.control_empty", "No control changes")));
				return;
			}
			foreach (WarControlDeltaState delta in deltas) {
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
			IReadOnlyList<WarProvinceTransferState> transfers = snapshot.TransferredProvinces
				?? Array.Empty<WarProvinceTransferState>();
			if (transfers.Count == 0) {
				_provincesList.Add(CreateRow(GetLoc("war_result.provinces_empty", "No provinces transferred")));
				return;
			}
			foreach (WarProvinceTransferState transfer in transfers) {
				_provincesList.Add(CreateProvinceTransferRow(transfer));
			}
		}

		VisualElement CreateProvinceTransferRow(WarProvinceTransferState transfer) {
			ProvinceTransferRowBuilder.Elements elements = ProvinceTransferRowBuilder.Build();
			ProvinceTransferRowBuilder.Bind(
				elements,
				_countryVisualConfig?.Find(transfer.OldOwnerCountryId)?.flag,
				_countryVisualConfig?.Find(transfer.NewOwnerCountryId)?.flag,
				GetProvinceName(transfer.ProvinceId));
			return elements.Row;
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
