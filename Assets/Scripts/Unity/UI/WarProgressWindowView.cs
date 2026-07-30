using System;
using System.Globalization;
using GS.Game.Common;
using GS.Main;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	public class WarProgressWindowView {
		const string AttackerColor = "#C84040";
		const string DefenderColor = "#4070C8";

		readonly VisualElement _root;
		readonly Label _title;
		readonly VisualElement _attackerFill;
		readonly VisualElement _defenderFill;
		readonly ScrollView _effectsList;
		readonly ScrollView _battlesList;
		readonly Label _battlesEmpty;
		readonly Label _effectsTitle;
		readonly Label _statsTitle;
		readonly Label _battlesTitle;
		readonly Label _attackerLabel;
		readonly Label _defenderLabel;
		readonly Label _attackerRecruits;
		readonly Label _attackerTroopsInBattles;
		readonly Label _attackerCasualties;
		readonly Label _attackerDamage;
		readonly Label _attackerDurability;
		readonly Label _defenderRecruits;
		readonly Label _defenderTroopsInBattles;
		readonly Label _defenderCasualties;
		readonly Label _defenderDamage;
		readonly Label _defenderDurability;
		readonly ILocalization _loc;
		Func<string, string, string> _getText = (_, fallback) => fallback;

		public WarProgressWindowView(VisualElement root, ILocalization loc) {
			_root = root;
			_loc = loc;
			_title = root.Q<Label>("war-progress-title");
			_attackerFill = root.Q<VisualElement>("progress-attacker-fill");
			_defenderFill = root.Q<VisualElement>("progress-defender-fill");
			_effectsList = root.Q<ScrollView>("effects-list");
			_battlesList = root.Q<ScrollView>("battles-list");
			_battlesEmpty = root.Q<Label>("battles-empty");
			_effectsTitle = root.Q<Label>("effects-title");
			_statsTitle = root.Q<Label>("stats-title");
			_battlesTitle = root.Q<Label>("battles-title");
			_attackerLabel = root.Q<Label>("attacker-label");
			_defenderLabel = root.Q<Label>("defender-label");
			_attackerRecruits = root.Q<Label>("attacker-recruits");
			_attackerTroopsInBattles = root.Q<Label>("attacker-troops-in-battles");
			_attackerCasualties = root.Q<Label>("attacker-casualties");
			_attackerDamage = root.Q<Label>("attacker-damage");
			_attackerDurability = root.Q<Label>("attacker-durability");
			_defenderRecruits = root.Q<Label>("defender-recruits");
			_defenderTroopsInBattles = root.Q<Label>("defender-troops-in-battles");
			_defenderCasualties = root.Q<Label>("defender-casualties");
			_defenderDamage = root.Q<Label>("defender-damage");
			_defenderDurability = root.Q<Label>("defender-durability");
		}

		public void RefreshStaticTexts(Func<string, string, string> getText) {
			_getText = getText ?? _getText;
			if (_effectsTitle != null) {
				_effectsTitle.text = _getText("war_progress.effects_title", "Progress effects");
			}
			if (_statsTitle != null) {
				_statsTitle.text = _getText("war_progress.stats_title", "Forces");
			}
			if (_battlesTitle != null) {
				_battlesTitle.text = _getText("war_progress.battles_title", "Battles");
			}
			if (_battlesEmpty != null) {
				_battlesEmpty.text = _getText("war_progress.battles_empty", "No battles yet");
			}
			if (_attackerLabel != null) {
				_attackerLabel.text = _getText("war_progress.attacker_label", "Attacker");
			}
			if (_defenderLabel != null) {
				_defenderLabel.text = _getText("war_progress.defender_label", "Defender");
			}
		}

		public void Refresh(SelectedWarState state) {
			if (state == null || !state.IsValid) {
				return;
			}

			if (_title != null) {
				_title.text = string.Format(
					GetLoc("hud.war.title_format", "{0} - {1} War"),
					GetCountryName(state.Attacker.CountryId),
					GetCountryName(state.Defender.CountryId));
			}

			UpdateProgressBar(state.Progress);
			RebuildEffectsList(state);
			UpdateSideStats(state.Attacker, _attackerRecruits, _attackerTroopsInBattles, _attackerCasualties, _attackerDamage, _attackerDurability);
			UpdateSideStats(state.Defender, _defenderRecruits, _defenderTroopsInBattles, _defenderCasualties, _defenderDamage, _defenderDurability);
			RebuildBattlesList(state);
		}

		void UpdateProgressBar(double progress) {
			if (_attackerFill != null) {
				float attackerPercent = (float)Math.Max(0, progress);
				_attackerFill.style.width = new Length(attackerPercent, LengthUnit.Percent);
			}
			if (_defenderFill != null) {
				float defenderPercent = (float)Math.Max(0, -progress);
				_defenderFill.style.width = new Length(defenderPercent, LengthUnit.Percent);
			}
		}

		void RebuildEffectsList(SelectedWarState state) {
			if (_effectsList == null) {
				return;
			}
			_effectsList.Clear();
			foreach (WarProgressHistoryEntryState entry in state.History) {
				var row = new Label(FormatEffectEntry(entry));
				row.AddToClassList("war-progress-effect-row");
				row.enableRichText = true;
				_effectsList.Add(row);
			}
		}

		string FormatEffectEntry(WarProgressHistoryEntryState entry) {
			string amount = FormatNumber(entry.AppliedDelta);
			if (entry.EffectId.StartsWith("war_progress_decay", StringComparison.Ordinal)) {
				return string.Format(GetLoc("war_progress.effect_decay_format", "Decay: {0}"), amount);
			}
			if (entry.EffectId.StartsWith("war_progress_battle_", StringComparison.Ordinal)) {
				return string.Format(GetLoc("war_progress.effect_battle_format", "Battle result: {0}"), amount);
			}
			return $"{entry.EffectId}: {amount}";
		}

		void UpdateSideStats(
			WarSideStatsState stats,
			Label recruits,
			Label troopsInBattles,
			Label casualties,
			Label damage,
			Label durability) {
			if (recruits != null) {
				recruits.text = $"{_getText("war_progress.recruits", "Recruits")}: {FormatNumber(stats.RecruitsAvailable)}";
			}
			if (troopsInBattles != null) {
				troopsInBattles.text = $"{_getText("war_progress.troops_in_battles", "In battles")}: {FormatNumber(stats.TroopsInBattles)}";
			}
			if (casualties != null) {
				casualties.text = $"{_getText("war_progress.casualties", "Casualties")}: {FormatNumber(stats.Casualties)}";
			}
			if (damage != null) {
				damage.text = $"{_getText("war_progress.damage", "Damage")}: {FormatNumber(stats.Damage)}";
			}
			if (durability != null) {
				durability.text = $"{_getText("war_progress.durability", "Durability")}: {FormatNumber(stats.Durability)}";
			}
		}

		void RebuildBattlesList(SelectedWarState state) {
			if (_battlesList == null) {
				return;
			}
			_battlesList.Clear();
			foreach (WarBattleRowState rowState in state.Battles) {
				var row = new Label(FormatBattleRow(rowState));
				row.AddToClassList("war-progress-battle-row");
				row.enableRichText = true;
				_battlesList.Add(row);
			}
			if (_battlesEmpty != null) {
				_battlesEmpty.style.display = state.Battles.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
			PinBattlesToBottom();
		}

		void PinBattlesToBottom() {
			if (_battlesList == null) {
				return;
			}
			EventCallback<GeometryChangedEvent> callback = null;
			callback = _ => {
				_battlesList.UnregisterCallback(callback);
				_battlesList.scrollOffset = new UnityEngine.Vector2(0, float.MaxValue);
			};
			_battlesList.RegisterCallback(callback);
		}

		string FormatBattleRow(WarBattleRowState row) {
			string provinceName = GetProvinceName(row.ProvinceId);
			if (row.IsFinished) {
				string winnerName = WrapSideColored(row.WinnerCountryId, row.WinnerSide);
				string attackerCasualties = WrapColored(FormatNumber(row.AttackerCasualties), AttackerColor);
				string defenderCasualties = WrapColored(FormatNumber(row.DefenderCasualties), DefenderColor);
				return string.Format(
					GetLoc("war_progress.battle_finished_format", "Battle at {0} ({1}, -{2} / -{3})"),
					provinceName,
					winnerName,
					attackerCasualties,
					defenderCasualties);
			}

			string progress = FormatNumber(row.Progress);
			string attackerTroops = WrapColored(FormatNumber(row.AttackerTroops), AttackerColor);
			string defenderTroops = WrapColored(FormatNumber(row.DefenderTroops), DefenderColor);
			return string.Format(
				GetLoc("war_progress.battle_active_format", "Battle at {0} [{1}] ({2} vs {3})"),
				provinceName,
				progress,
				attackerTroops,
				defenderTroops);
		}

		string WrapSideColored(string countryId, WarParticipantKind side) {
			string color = side == WarParticipantKind.Attacker ? AttackerColor : DefenderColor;
			return WrapColored(GetCountryName(countryId), color);
		}

		static string WrapColored(string text, string hexColor) {
			return $"<color={hexColor}>{text}</color>";
		}

		string GetCountryName(string countryId) {
			string key = $"country_name.{countryId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}
			return countryId;
		}

		string GetProvinceName(string provinceId) {
			string key = $"province_name.{provinceId}";
			string localized = _loc?.Get(key) ?? "";
			if (!string.IsNullOrEmpty(localized) && localized != key) {
				return localized;
			}
			return provinceId;
		}

		string GetLoc(string key, string fallback) {
			return _getText(key, fallback);
		}

		static string FormatNumber(double value) {
			return value.ToString("0.#", CultureInfo.InvariantCulture);
		}
	}
}
