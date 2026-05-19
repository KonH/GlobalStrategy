using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;
using GS.Unity.Common;

namespace GS.Unity.UI {
	class OrgCharactersView {
		readonly VisualElement _container;
		readonly ILocalization _loc;
		readonly CharacterConfig _characterConfig;
		readonly TooltipSystem _tooltip;
		readonly CharacterVisualConfig _visualConfig;

		public OrgCharactersView(VisualElement container, ILocalization loc, CharacterConfig characterConfig, TooltipSystem tooltip, CharacterVisualConfig visualConfig) {
			_container = container;
			_loc = loc;
			_characterConfig = characterConfig;
			_tooltip = tooltip;
			_visualConfig = visualConfig;
		}

		public void Refresh(OrgCharactersState state) {
			_container.Clear();
			if (state.Slots.Count == 0) {
				_container.style.display = DisplayStyle.None;
				return;
			}
			_container.style.display = DisplayStyle.Flex;
			foreach (var slot in state.Slots) {
				_container.Add(slot.Character != null
					? BuildFilledCard(slot)
					: BuildEmptyCard(slot));
			}
		}

		VisualElement BuildFilledCard(OrgCharacterSlotEntry slot) {
			var entry = slot.Character!;
			var card = new VisualElement();
			card.AddToClassList("org-char-card");

			var portrait = new VisualElement();
			portrait.AddToClassList("org-portrait-area");
			var sprite = _visualConfig?.FindPortrait(entry.CharacterId);
			if (sprite != null) {
				portrait.style.backgroundImage = new StyleBackground(sprite);
			}
			card.Add(portrait);

			var roleDef = _characterConfig.FindRole(entry.RoleId);
			string roleName = roleDef != null ? _loc.Get(roleDef.NameKey) : entry.RoleId;
			string roleDesc = roleDef != null ? _loc.Get(roleDef.DescriptionKey) : "";

			var infoBlock = new VisualElement();
			infoBlock.AddToClassList("org-info-block");

			var nameParts = new System.Collections.Generic.List<string>();
			foreach (var key in entry.NamePartKeys) {
				nameParts.Add(_loc.Get(key));
			}
			var nameLabel = new Label(string.Join(" ", nameParts));
			nameLabel.AddToClassList("org-char-name");
			infoBlock.Add(nameLabel);

			var roleLabel = new Label(roleName);
			roleLabel.AddToClassList("org-char-role");
			infoBlock.Add(roleLabel);

			var statsBlock = new VisualElement();
			statsBlock.AddToClassList("org-char-stats");
			var roleSkillIds = roleDef != null
				? new HashSet<string>(roleDef.SkillIds)
				: new HashSet<string>();

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
				string csn = skillName;
				string csd = skillDesc;
				_tooltip.RegisterTrigger(chip, $"skill-{skill.SkillId}-{entry.CharacterId}", _ => BuildSimpleTooltip(csn, csd), new HashSet<string>());
				statsBlock.Add(chip);
			}
			infoBlock.Add(statsBlock);
			card.Add(infoBlock);

			if (!string.IsNullOrEmpty(roleDesc)) {
				string capturedDesc = roleDesc;
				string capturedRoleName = roleName;
				_tooltip.RegisterTrigger(card, $"role-{entry.RoleId}-{entry.CharacterId}", _ => BuildSimpleTooltip(capturedRoleName, capturedDesc), new HashSet<string>());
			}

			return card;
		}

		VisualElement BuildEmptyCard(OrgCharacterSlotEntry slot) {
			var card = new VisualElement();
			card.AddToClassList("org-char-card");
			card.AddToClassList("org-char-card--empty");

			var portrait = new VisualElement();
			portrait.AddToClassList("org-portrait-area");
			portrait.AddToClassList("org-portrait-area--empty");
			card.Add(portrait);

			var roleDef = _characterConfig.FindRole(slot.RoleId);
			string roleName = roleDef != null ? _loc.Get(roleDef.NameKey) : slot.RoleId;

			var infoBlock = new VisualElement();
			infoBlock.AddToClassList("org-info-block");

			var roleLabel = new Label(roleName);
			roleLabel.AddToClassList("org-char-role");
			infoBlock.Add(roleLabel);

			string statusKey = slot.IsAvailable ? "hud.slot_available" : "hud.slot_empty";
			var statusLabel = new Label(_loc.Get(statusKey));
			statusLabel.AddToClassList("gs-hint");
			infoBlock.Add(statusLabel);

			card.Add(infoBlock);
			return card;
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
