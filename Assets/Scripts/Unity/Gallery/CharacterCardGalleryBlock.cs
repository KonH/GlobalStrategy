using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Game.Configs;
using GS.Unity.Common;
using GS.Unity.UI;

namespace GS.Unity.Gallery {
	/// <summary>Previews the CharacterCard component in its player, org-filled and org-empty shapes.</summary>
	public class CharacterCardGalleryBlock : GalleryBlockBase {
		static readonly List<string> _states = new List<string> { "Player card", "Org card (filled)", "Org card (empty)" };

		readonly ILocalization _loc;
		readonly CharacterConfig _config;
		readonly CharacterVisualConfig _visualConfig;
		readonly List<string> _roleIds = new();

		public override string Id => "character-card";
		public override string Title => "Card: CharacterCard";
		protected override IReadOnlyList<string> InstanceChoices => _roleIds;
		protected override IReadOnlyList<string> StateChoices => _states;
		protected override string InstanceLabel => "Role";

		public CharacterCardGalleryBlock(ILocalization loc, TextAsset characterConfigAsset, CharacterVisualConfig visualConfig) {
			_loc = loc;
			_visualConfig = visualConfig;
			if (characterConfigAsset != null) {
				_config = JsonConvert.DeserializeObject<CharacterConfig>(characterConfigAsset.text);
			}
			if (_config != null) {
				foreach (CharacterRoleDefinition role in _config.Roles) {
					_roleIds.Add(role.RoleId);
				}
			}
		}

		protected override void Render(VisualElement stage, string roleId, int stateIndex) {
			if (_config == null) {
				return;
			}
			CharacterRoleDefinition role = _config.FindRole(roleId);
			string roleName = role != null ? _loc.Get(role.NameKey) : roleId;

			VisualElement card;
			if (stateIndex == 0) {
				card = BuildPlayerCard(role, roleName);
			} else if (stateIndex == 1) {
				card = BuildOrgCard(role, roleName, empty: false);
			} else {
				card = BuildOrgCard(role, roleName, empty: true);
			}
			stage.Add(card);
		}

		VisualElement BuildPlayerCard(CharacterRoleDefinition role, string roleName) {
			CharacterCardBuilder.PlayerElements elements = CharacterCardBuilder.BuildPlayer();
			elements.Name.text = "Jane Doe";
			elements.Role.text = roleName;
			CharacterCardBuilder.BindOpinion(elements.Opinion, 12);
			AddSampleSkillChips(elements.StatsBlock, role);
			return elements.Card;
		}

		VisualElement BuildOrgCard(CharacterRoleDefinition role, string roleName, bool empty) {
			CharacterCardBuilder.OrgElements elements = CharacterCardBuilder.BuildOrg(empty);
			elements.Role.text = roleName;
			if (empty) {
				elements.Status.text = _loc.Get("hud.slot_empty");
			} else {
				elements.Name.text = "John Smith";
				AddSampleSkillChips(elements.StatsBlock, role);
			}
			return elements.Card;
		}

		void AddSampleSkillChips(VisualElement statsBlock, CharacterRoleDefinition role) {
			if (role == null) {
				return;
			}
			foreach (string skillId in role.SkillIds) {
				CharacterSkillDefinition skillDef = _config.FindSkill(skillId);
				StatChipBuilder.Elements chip = StatChipBuilder.Build();
				StatChipBuilder.Bind(chip, "50", $"character-skill-icon--{skillId}");
				statsBlock.Add(chip.Chip);
			}
		}
	}
}
