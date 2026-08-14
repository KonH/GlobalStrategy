#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class CharactersView {
		readonly VisualElement _container;
		readonly ILocalization _loc;
		readonly CharacterConfig _characterConfig;
		readonly TooltipSystem _tooltip;
		readonly CharacterVisualConfig? _visualConfig;
		readonly ActionConfig _actionConfig;
		readonly ActionVisualConfig _actionVisualConfig;

		public CharactersView(VisualElement container, ILocalization loc, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig visualConfig, ActionConfig actionConfig, ActionVisualConfig actionVisualConfig) {
			_container = container;
			_loc = loc;
			_characterConfig = characterConfig;
			_tooltip = tooltip;
			_visualConfig = visualConfig;
			_actionConfig = actionConfig;
			_actionVisualConfig = actionVisualConfig;
			if (_visualConfig == null) {
				Debug.LogError("[CharactersView] CharacterVisualConfig is null — portraits will not display. Assign the asset in GameLifetimeScope.");
			}
		}

		public void Refresh(CountryCharactersState state) {
			_container.Clear();
			if (state.Characters.Count == 0) {
				_container.style.display = DisplayStyle.None;
				return;
			}
			_container.style.display = DisplayStyle.Flex;
			foreach (var entry in state.Characters) {
				_container.Add(BuildCharacterCard(entry));
			}
		}

		public VisualElement? FindCardByRole(string roleId) {
			if (string.IsNullOrEmpty(roleId)) {
				return null;
			}
			foreach (var child in _container.Children()) {
				if (child.userData is string cardRoleId && cardRoleId == roleId) {
					return child;
				}
			}
			return null;
		}

		VisualElement BuildCharacterCard(CharacterStateEntry entry) {
			var card = new VisualElement();
			card.AddToClassList("char-card");
			card.userData = entry.RoleId;

			var roleDef = _characterConfig.FindRole(entry.RoleId);
			string roleName = roleDef != null ? _loc.Get(roleDef.NameKey) : entry.RoleId;
			string roleDesc = roleDef != null ? _loc.Get(roleDef.DescriptionKey) : "";

			var portrait = new VisualElement();
			portrait.AddToClassList("char-portrait-area");
			var sprite = _visualConfig?.FindPortrait(entry.CharacterId);
			if (sprite != null) {
				portrait.style.backgroundImage = new StyleBackground(sprite);
			}
			card.Add(portrait);

			var nameParts = new System.Collections.Generic.List<string>();
			foreach (var key in entry.NamePartKeys) {
				nameParts.Add(_loc.Get(key));
			}
			var nameOverlay = new VisualElement();
			nameOverlay.AddToClassList("char-name-overlay");
			var nameLabel = new Label(string.Join(" ", nameParts));
			nameLabel.AddToClassList("char-name");
			nameOverlay.Add(nameLabel);
			portrait.Add(nameOverlay);

			var info = new VisualElement();
			info.AddToClassList("char-info");

			var roleLabel = new Label(roleName);
			roleLabel.AddToClassList("char-role");
			info.Add(roleLabel);

			int displayOpinion = entry.Opinion.Display;
			string opinionText = displayOpinion >= 0 ? $"+{displayOpinion}" : $"{displayOpinion}";
			var opinionLabel = new Label(opinionText);
			opinionLabel.AddToClassList("char-opinion");
			opinionLabel.AddToClassList(displayOpinion < 0 ? "gs-color-negative" : "gs-color-positive");
			info.Add(opinionLabel);

			var statsBlock = new VisualElement();
			statsBlock.AddToClassList("char-stats");

			var roleSkillIds = roleDef != null
				? new System.Collections.Generic.HashSet<string>(roleDef.SkillIds)
				: new System.Collections.Generic.HashSet<string>();

			foreach (var skillDef in _characterConfig.Skills) {
				if (!roleSkillIds.Contains(skillDef.SkillId)) { continue; }
				SkillEntry? skill = null;
				foreach (var s in entry.Skills) {
					if (s.SkillId == skillDef.SkillId) { skill = s; break; }
				}
				if (skill == null) { continue; }

				string skillName = _loc.Get(skillDef.NameKey);
				string skillDesc = _loc.Get(skillDef.DescriptionKey);

				var chip = new VisualElement();
				chip.AddToClassList("char-stat-chip");
				var skillIcon = new VisualElement();
				skillIcon.AddToClassList("char-stat-icon");
				skillIcon.AddToClassList(GetSkillTintClass(skill.SkillId));
				skillIcon.AddToClassList($"character-skill-icon--{skill.SkillId}");
				chip.Add(skillIcon);
				var valueLabel = new Label(skill.Value.ToString());
				chip.Add(valueLabel);

				string capturedSkillName = skillName;
				string capturedSkillDesc = skillDesc;
				_tooltip.RegisterTrigger(chip, $"skill-{skill.SkillId}-{entry.CharacterId}", _ => BuildSimpleTooltip(capturedSkillName, capturedSkillDesc), new System.Collections.Generic.HashSet<string>());

				statsBlock.Add(chip);
			}
			info.Add(statsBlock);
			card.Add(info);

			var hintRows = CharacterCardHintProjector.Build(_actionConfig, entry.RoleId);
			foreach (var row in hintRows) {
				row.IsMet = entry.Opinion.Display >= row.Threshold;
			}

			if (!string.IsNullOrEmpty(roleDesc) || hintRows.Count > 0) {
				string capturedDesc = roleDesc;
				string capturedRoleName = roleName;
				string capturedCharacterId = entry.CharacterId;
				_tooltip.RegisterTrigger(card, $"role-{entry.RoleId}-{entry.CharacterId}",
					context => BuildRoleTooltip(context, capturedRoleName, capturedDesc, capturedCharacterId, hintRows),
					new System.Collections.Generic.HashSet<string>());
			}

			return card;
		}

		VisualElement BuildRoleTooltip(TooltipContext context, string roleName, string roleDesc, string characterId, List<CharacterCardHintRowState> hintRows) {
			var root = BuildSimpleTooltip(roleName, roleDesc);
			if (hintRows.Count == 0) {
				return root;
			}

			var rowsContainer = new VisualElement();
			rowsContainer.AddToClassList("character-hint-rows");
			foreach (var row in hintRows) {
				var def = _actionConfig.Find(row.ActionId);
				string cardName = ResolveActionDisplayName(def, row.ActionId);
				string rowText = string.Format(_loc.Get("hud.character.card_hint"), row.Threshold, cardName);

				var rowLabel = new Label(rowText);
				rowLabel.AddToClassList("tooltip-effect-name");
				rowLabel.AddToClassList("tooltip-inner-trigger");
				// Use tooltip-effect-* (declared after tooltip-effect-name) — gs-color-*
				// loses to tooltip-effect-name's later color rule.
				rowLabel.EnableInClassList("tooltip-effect-positive", row.IsMet);
				rowLabel.EnableInClassList("tooltip-effect-hint", !row.IsMet);
				rowsContainer.Add(rowLabel);

				string capturedActionId = row.ActionId;
				context.RegisterInnerTrigger(rowLabel, $"card-hint-{characterId}-{capturedActionId}", _ => BuildCardPreview(capturedActionId));
			}
			root.Add(rowsContainer);
			return root;
		}

		VisualElement BuildCardPreview(string actionId) {
			var def = _actionConfig.Find(actionId);
			string name = ResolveActionDisplayName(def, actionId);
			string desc = def != null ? _loc.Get(def.DescKey) : "";
			string? goldCostText = GetGoldCostText(def);
			var sprite = _actionVisualConfig?.FindFront(actionId);

			var result = ActionCardBuilder.Build(name, desc, goldCostText, sprite);
			result.Card.AddToClassList("action-card--available");
			return result.Card;
		}

		string ResolveActionDisplayName(ActionDefinition? def, string fallbackId) {
			if (def == null) {
				return fallbackId;
			}
			string raw = _loc.Get(def.NameKey);
			if (raw.IndexOf("{0}", System.StringComparison.Ordinal) < 0) {
				return raw;
			}
			return string.Format(raw, _loc.Get("hud.character.card_country_placeholder"));
		}

		static double GetGoldCost(ActionDefinition? def) {
			if (def == null) { return 0; }
			foreach (var c in def.Cost) {
				if (c.ResourceId == "gold") { return c.Amount; }
			}
			return 0;
		}

		static string? GetGoldCostText(ActionDefinition? def) {
			double gold = GetGoldCost(def);
			if (gold == 0) { return null; }
			return gold == System.Math.Floor(gold) ? $"{(int)gold}" : $"{gold:F1}";
		}

		static string GetSkillTintClass(string skillId) {
			switch (skillId) {
				case "power": return "gs-icon--tint-mil";
				case "charm": return "gs-icon--tint-dip";
				case "stinginess": return "gs-icon--tint-eco";
				case "intrigue": return "gs-icon--tint-sec";
				default: return "gs-icon--tint-light";
			}
		}

		VisualElement BuildSimpleTooltip(string header, string body) {
			var root = new VisualElement();
			var headerLabel = new Label(header);
			headerLabel.AddToClassList("tooltip-header");
			root.Add(headerLabel);
			if (!string.IsNullOrEmpty(body)) {
				var bodyLabel = new Label(body);
				bodyLabel.AddToClassList("tooltip-effect-name");
				root.Add(bodyLabel);
			}
			return root;
		}
	}
}
