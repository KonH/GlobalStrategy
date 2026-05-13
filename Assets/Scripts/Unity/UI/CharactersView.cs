using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Configs;

namespace GS.Unity.UI {
	class CharactersView {
		readonly VisualElement _container;
		readonly ILocalization _loc;
		readonly CharacterConfig _characterConfig;
		readonly TooltipSystem _tooltip;

		public CharactersView(VisualElement container, ILocalization loc, CharacterConfig characterConfig, TooltipSystem tooltip) {
			_container = container;
			_loc = loc;
			_characterConfig = characterConfig;
			_tooltip = tooltip;
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

		VisualElement BuildCharacterCard(CharacterStateEntry entry) {
			var card = new VisualElement();
			card.AddToClassList("character-card");

			var roleDef = _characterConfig.FindRole(entry.RoleId);
			string roleName = roleDef != null ? _loc.Get(roleDef.NameKey) : entry.RoleId;
			string roleDesc = roleDef != null ? _loc.Get(roleDef.DescriptionKey) : "";

			var nameParts = new List<string>();
			foreach (var key in entry.NamePartKeys) {
				nameParts.Add(_loc.Get(key));
			}
			var nameLabel = new Label(string.Join(" ", nameParts));
			nameLabel.AddToClassList("character-name");
			card.Add(nameLabel);

			var portrait = new VisualElement();
			portrait.AddToClassList("character-portrait");
			card.Add(portrait);

			var roleBlock = new VisualElement();
			roleBlock.AddToClassList("role-block");
			var roleIcon = new VisualElement();
			roleIcon.AddToClassList($"character-role-icon--{entry.RoleId}");
			roleBlock.Add(roleIcon);
			var roleLabel = new Label(roleName);
			roleLabel.AddToClassList("gs-hint");
			roleBlock.Add(roleLabel);
			card.Add(roleBlock);

			if (!string.IsNullOrEmpty(roleDesc)) {
				string capturedDesc = roleDesc;
				_tooltip.RegisterTrigger(roleBlock, $"role-{entry.RoleId}-{entry.CharacterId}", _ => BuildSimpleTooltip(roleName, capturedDesc), new System.Collections.Generic.HashSet<string>());
			}

			var skillsBlock = new VisualElement();
			skillsBlock.AddToClassList("skills-block");

			var roleSkillIds = roleDef != null
				? new System.Collections.Generic.HashSet<string>(roleDef.SkillIds)
				: new System.Collections.Generic.HashSet<string>();

			foreach (var skillDef in _characterConfig.Skills) {
				if (!roleSkillIds.Contains(skillDef.SkillId)) {
					continue;
				}
				SkillEntry skill = null;
				foreach (var s in entry.Skills) {
					if (s.SkillId == skillDef.SkillId) { skill = s; break; }
				}
				if (skill == null) {
					continue;
				}
				string skillName = _loc.Get(skillDef.NameKey);
				string skillDesc = _loc.Get(skillDef.DescriptionKey);

				var chip = new VisualElement();
				chip.AddToClassList("skill-chip");
				var skillIcon = new VisualElement();
				skillIcon.AddToClassList($"character-skill-icon--{skill.SkillId}");
				chip.Add(skillIcon);
				var valueLabel = new Label(skill.Value.ToString());
				valueLabel.AddToClassList("skill-value");
				chip.Add(valueLabel);

				string capturedSkillName = skillName;
				string capturedSkillDesc = skillDesc;
				_tooltip.RegisterTrigger(chip, $"skill-{skill.SkillId}-{entry.CharacterId}", _ => BuildSimpleTooltip(capturedSkillName, capturedSkillDesc), new System.Collections.Generic.HashSet<string>());

				skillsBlock.Add(chip);
			}
			card.Add(skillsBlock);

			return card;
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
