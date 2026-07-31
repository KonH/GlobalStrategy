using System;
using System.Collections.Generic;
using System.Globalization;
using GS.Game.Common;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Map;
using UnityEngine;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	class WarProgressWindowView {
		const string AttackerColor = "#C84040";
		const string DefenderColor = "#4070C8";
		const string BattleCategoryKey = "battle";
		const string DecayCategoryKey = "decay";
		const string BattleEffectPrefix = "war_progress_battle_";
		const string DecayEffectId = "war_progress_decay";

		sealed class GroupedEffect {
			public string CategoryKey;
			public double Total;
			public readonly List<(string BattleId, double Delta)> Battles = new();
		}

		readonly VisualElement _root;
		readonly ILocalization _loc;
		readonly CountryVisualConfig _countryVisualConfig;
		readonly EffectConfig _effectConfig;
		readonly TooltipSystem _tooltip;
		readonly Label _title;
		readonly Label _progressValue;
		readonly VisualElement _attackerFill;
		readonly VisualElement _defenderFill;
		readonly VisualElement _attackerFlag;
		readonly VisualElement _defenderFlag;
		readonly ScrollView _attackerEffectsList;
		readonly ScrollView _defenderEffectsList;
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
		Func<string, string, string> _getText = (_, fallback) => fallback;

		public WarProgressWindowView(
			VisualElement root, ILocalization loc, CountryVisualConfig countryVisualConfig,
			EffectConfig effectConfig, TooltipSystem tooltip) {
			_root = root;
			_loc = loc;
			_countryVisualConfig = countryVisualConfig;
			_effectConfig = effectConfig;
			_tooltip = tooltip;
			_title = root.Q<Label>("war-progress-title");
			_progressValue = root.Q<Label>("war-progress-value");
			_attackerFill = root.Q<VisualElement>("progress-attacker-fill");
			_defenderFill = root.Q<VisualElement>("progress-defender-fill");
			_attackerFlag = root.Q<VisualElement>("attacker-flag");
			_defenderFlag = root.Q<VisualElement>("defender-flag");
			_attackerEffectsList = root.Q<ScrollView>("attacker-effects-list");
			_defenderEffectsList = root.Q<ScrollView>("defender-effects-list");
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
			if (_progressValue != null) {
				_progressValue.enableRichText = true;
			}
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
			UpdateFlag(_attackerFlag, state.Attacker.CountryId);
			UpdateFlag(_defenderFlag, state.Defender.CountryId);
			RebuildEffectsList(state);
			UpdateSideStats(state.Attacker, _attackerRecruits, _attackerTroopsInBattles, _attackerCasualties, _attackerDamage, _attackerDurability, "attacker");
			UpdateSideStats(state.Defender, _defenderRecruits, _defenderTroopsInBattles, _defenderCasualties, _defenderDamage, _defenderDurability, "defender");
			RebuildBattlesList(state);
		}

		void UpdateProgressBar(double progress) {
			double clampedProgress = Math.Clamp(progress, -100, 100);
			float attackerPercent = (float)((clampedProgress + 100) / 2);
			float defenderPercent = 100f - attackerPercent;
			if (_attackerFill != null) {
				_attackerFill.style.width = new Length(attackerPercent, LengthUnit.Percent);
			}
			if (_defenderFill != null) {
				_defenderFill.style.width = new Length(defenderPercent, LengthUnit.Percent);
			}
			if (_progressValue != null) {
				string color = progress >= 0 ? AttackerColor : DefenderColor;
				_progressValue.text = WrapColored(FormatSigned(progress), color);
			}
		}

		void UpdateFlag(VisualElement flagElement, string countryId) {
			if (flagElement == null) {
				return;
			}
			Sprite sprite = _countryVisualConfig?.Find(countryId)?.flag;
			if (sprite == null) {
				flagElement.style.display = DisplayStyle.None;
				return;
			}
			flagElement.style.backgroundImage = new StyleBackground(sprite);
			flagElement.style.display = DisplayStyle.Flex;
		}

		void RebuildEffectsList(SelectedWarState state) {
			if (_attackerEffectsList == null || _defenderEffectsList == null) {
				return;
			}

			var battlesById = new Dictionary<string, WarBattleRowState>(StringComparer.Ordinal);
			foreach (WarBattleRowState battle in state.Battles) {
				battlesById[battle.BattleId] = battle;
			}

			var attackerGroups = new List<GroupedEffect>();
			var attackerIndex = new Dictionary<string, int>(StringComparer.Ordinal);
			var defenderGroups = new List<GroupedEffect>();
			var defenderIndex = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (WarProgressHistoryEntryState entry in state.History) {
				if (entry.AppliedDelta == 0) {
					continue;
				}
				bool favorsAttacker = entry.AppliedDelta > 0;
				List<GroupedEffect> groups = favorsAttacker ? attackerGroups : defenderGroups;
				Dictionary<string, int> index = favorsAttacker ? attackerIndex : defenderIndex;
				(string categoryKey, string battleId) = ClassifyEffect(entry.EffectId);
				if (!index.TryGetValue(categoryKey, out int existingIndex)) {
					existingIndex = groups.Count;
					index[categoryKey] = existingIndex;
					groups.Add(new GroupedEffect { CategoryKey = categoryKey });
				}
				GroupedEffect group = groups[existingIndex];
				group.Total += entry.AppliedDelta;
				if (battleId != null) {
					group.Battles.Add((battleId, entry.AppliedDelta));
				}
			}

			RenderEffectsColumn(_attackerEffectsList, attackerGroups, battlesById, "attacker");
			RenderEffectsColumn(_defenderEffectsList, defenderGroups, battlesById, "defender");
		}

		static (string categoryKey, string battleId) ClassifyEffect(string effectId) {
			if (effectId == DecayEffectId) {
				return (DecayCategoryKey, null);
			}
			if (effectId.StartsWith(BattleEffectPrefix, StringComparison.Ordinal)) {
				return (BattleCategoryKey, effectId.Substring(BattleEffectPrefix.Length));
			}
			return (effectId, null);
		}

		void RenderEffectsColumn(
			ScrollView list, List<GroupedEffect> groups, Dictionary<string, WarBattleRowState> battlesById, string sideKey) {
			list.Clear();
			foreach (GroupedEffect group in groups) {
				var row = new Label(FormatGroupedEffect(group));
				row.AddToClassList("war-progress-effect-row");
				row.enableRichText = true;
				if (group.CategoryKey == BattleCategoryKey && group.Battles.Count > 0) {
					GroupedEffect capturedGroup = group;
					_tooltip?.RegisterTrigger(
						row,
						$"war-progress-effect-{sideKey}-battle",
						_ => BuildBattleGroupTooltip(capturedGroup, battlesById),
						new HashSet<string>());
				}
				list.Add(row);
			}
		}

		string FormatGroupedEffect(GroupedEffect group) {
			string amount = FormatSigned(group.Total);
			if (group.CategoryKey == DecayCategoryKey) {
				return string.Format(GetLoc("war_progress.effect_decay_format", "Decay: {0}"), amount);
			}
			if (group.CategoryKey == BattleCategoryKey) {
				return string.Format(GetLoc("war_progress.effect_battle_format", "Battle result: {0}"), amount);
			}
			return $"{group.CategoryKey}: {amount}";
		}

		VisualElement BuildBattleGroupTooltip(GroupedEffect group, Dictionary<string, WarBattleRowState> battlesById) {
			var content = new VisualElement();
			var titleLabel = new Label(GetLoc("war_progress.effect_battle_tooltip_title", "Related battles"));
			titleLabel.AddToClassList("tooltip-header");
			content.Add(titleLabel);
			foreach ((string battleId, double delta) in group.Battles) {
				string provinceName = battlesById.TryGetValue(battleId, out WarBattleRowState row)
					? GetProvinceName(row.ProvinceId)
					: battleId;
				var rowLabel = new Label(string.Format(
					GetLoc("war_progress.effect_battle_tooltip_row_format", "{0}: {1}"),
					provinceName,
					FormatSigned(delta)));
				rowLabel.AddToClassList("tooltip-effect-name");
				content.Add(rowLabel);
			}
			return content;
		}

		void UpdateSideStats(
			WarSideStatsState stats,
			Label recruits,
			Label troopsInBattles,
			Label casualties,
			Label damage,
			Label durability,
			string sideKey) {
			if (recruits != null) {
				recruits.text = $"{_getText("war_progress.recruits", "Recruits")}: {FormatResourceValue(stats.RecruitsAvailable)}";
			}
			if (troopsInBattles != null) {
				troopsInBattles.text = $"{_getText("war_progress.troops_in_battles", "In battles")}: {FormatResourceValue(stats.TroopsInBattles)}";
			}
			if (casualties != null) {
				casualties.text = $"{_getText("war_progress.casualties", "Casualties")}: {FormatResourceValue(stats.Casualties)}";
			}
			if (damage != null) {
				damage.text = $"{_getText("war_progress.damage", "Damage")}: {FormatResourceValue(stats.Damage)}";
				WarSideStatsState capturedStats = stats;
				_tooltip?.RegisterTrigger(
					damage, $"war-progress-damage-{sideKey}", _ => BuildDamageTooltip(capturedStats), new HashSet<string>());
			}
			if (durability != null) {
				durability.text = $"{_getText("war_progress.durability", "Durability")}: {FormatResourceValue(stats.Durability)}";
				WarSideStatsState capturedStats = stats;
				_tooltip?.RegisterTrigger(
					durability, $"war-progress-durability-{sideKey}", _ => BuildDurabilityTooltip(capturedStats), new HashSet<string>());
			}
		}

		VisualElement BuildDamageTooltip(WarSideStatsState stats) {
			var root = new VisualElement();

			var header = new Label(_getText("war_progress.damage", "Damage"));
			header.AddToClassList("tooltip-header");
			root.Add(header);

			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_base", "Country base: {0}"), FormatResourceValue(stats.DamageBase)));
			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_ruler", "Ruler skill: {0}"), FormatSigned(stats.DamageRulerBonus)));
			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_military_advisor", "Military advisor skill: {0}"), FormatSigned(stats.DamageAdvisorBonus)));

			if (stats.DamageBonusEffects.Count > 0) {
				double subtotal = stats.DamageBase + stats.DamageRulerBonus + stats.DamageAdvisorBonus;
				AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_subtotal", "Subtotal: {0}"), FormatResourceValue(subtotal)));

				foreach (EffectStateEntry effect in stats.DamageBonusEffects) {
					AddGainEffectRow(root, effect);
				}
				foreach (EffectStateEntry effect in stats.DamageBonusEffects) {
					AddDecayEffectRow(root, effect);
				}
			}

			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_total", "Total: {0}"), FormatResourceValue(stats.Damage)));

			return root;
		}

		VisualElement BuildDurabilityTooltip(WarSideStatsState stats) {
			var root = new VisualElement();

			var header = new Label(_getText("war_progress.durability", "Durability"));
			header.AddToClassList("tooltip-header");
			root.Add(header);

			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_base", "Country base: {0}"), FormatResourceValue(stats.DurabilityBase)));
			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_ruler", "Ruler skill: {0}"), FormatSigned(stats.DurabilityRulerBonus)));
			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_economic_advisor", "Economic advisor skill: {0}"), FormatSigned(stats.DurabilityAdvisorBonus)));
			AddTooltipRow(root, string.Format(GetLoc("war_progress.stat_tooltip_total", "Total: {0}"), FormatResourceValue(stats.Durability)));

			return root;
		}

		static void AddTooltipRow(VisualElement root, string text) {
			var row = new Label(text);
			row.AddToClassList("tooltip-effect-name");
			root.Add(row);
		}

		void AddGainEffectRow(VisualElement root, EffectStateEntry effect) {
			ActionEffectDefinition effectDef = FindEffectDefinition(effect.EffectId);
			string effectName = effectDef != null ? _loc.Get(effectDef.NameKey) : effect.EffectId;
			string text = string.Format(
				GetLoc("war_progress.stat_tooltip_effect_gain_format", "{0} by {1}: +{2}%"),
				effectName, effect.OrgDisplayName, FormatResourceValue(effect.MaxTotal));
			string description = effectDef != null ? _loc.Get(effectDef.DescKey) : null;
			AddEffectRow(root, text, description, positive: true);
		}

		void AddDecayEffectRow(VisualElement root, EffectStateEntry effect) {
			ActionEffectDefinition effectDef = FindEffectDefinition(effect.EffectId);
			string effectName = effectDef != null ? _loc.Get(effectDef.NameKey) : effect.EffectId;
			string text = string.Format(
				GetLoc("war_progress.stat_tooltip_effect_decay_format", "{0} decay by {1}: {2}%/month"),
				effectName, effect.OrgDisplayName, FormatSigned(effect.Value));
			string description = GetLoc("war_progress.stat_tooltip_effect_decay_desc", "This bonus fades gradually over time.");
			AddEffectRow(root, text, description, positive: false);
		}

		static void AddEffectRow(VisualElement root, string text, string description, bool positive) {
			var effectRow = new VisualElement();
			effectRow.AddToClassList("tooltip-effect-row");

			var nameLabel = new Label(text);
			nameLabel.AddToClassList("tooltip-effect-name");
			nameLabel.AddToClassList(positive ? "tooltip-effect-positive" : "tooltip-effect-negative");
			effectRow.Add(nameLabel);

			if (!string.IsNullOrEmpty(description)) {
				var descLabel = new Label(description);
				descLabel.AddToClassList("tooltip-description");
				effectRow.Add(descLabel);
			}

			root.Add(effectRow);
		}

		ActionEffectDefinition FindEffectDefinition(string dynamicEffectId) {
			if (_effectConfig == null) {
				return null;
			}
			foreach (ActionEffectDefinition candidate in _effectConfig.Effects) {
				if (!string.IsNullOrEmpty(candidate.EffectId) && dynamicEffectId.Contains(candidate.EffectId)) {
					return candidate;
				}
			}
			return null;
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
				string attackerCasualties = WrapColored($"-{FormatResourceValue(row.AttackerCasualties)}", AttackerColor);
				string defenderCasualties = WrapColored($"-{FormatResourceValue(row.DefenderCasualties)}", DefenderColor);
				return string.Format(
					GetLoc("war_progress.battle_finished_format", "Battle at {0} ({1} won, {2} / {3})"),
					provinceName,
					winnerName,
					attackerCasualties,
					defenderCasualties);
			}

			string progress = FormatSigned(row.Progress);
			string attackerTroops = WrapColored(FormatResourceValue(row.AttackerTroops), AttackerColor);
			string defenderTroops = WrapColored(FormatResourceValue(row.DefenderTroops), DefenderColor);
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

		static string FormatResourceValue(double value) {
			double roundedValue = Math.Round(value, MidpointRounding.AwayFromZero);
			double magnitude = Math.Abs(roundedValue);
			if (magnitude < 1_000) {
				return roundedValue.ToString("0", CultureInfo.InvariantCulture);
			}

			double divisor = magnitude < 1_000_000 ? 1_000 : 1_000_000;
			string suffix = magnitude < 1_000_000 ? "K" : "M";
			double scaledValue = Math.Round(roundedValue / divisor, MidpointRounding.AwayFromZero);
			if (suffix == "K" && Math.Abs(scaledValue) >= 1_000) {
				scaledValue = Math.Round(roundedValue / 1_000_000, MidpointRounding.AwayFromZero);
				suffix = "M";
			}

			return $"{scaledValue.ToString("0", CultureInfo.InvariantCulture)}{suffix}";
		}

		static string FormatSigned(double value) {
			string formatted = FormatResourceValue(value);
			return value > 0 ? $"+{formatted}" : formatted;
		}
	}
}
