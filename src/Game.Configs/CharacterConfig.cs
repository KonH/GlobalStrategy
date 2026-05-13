using System.Collections.Generic;

namespace GS.Game.Configs {
	public class CharacterSkillDefinition {
		public string SkillId { get; set; } = "";
		public string NameKey { get; set; } = "";
		public string DescriptionKey { get; set; } = "";
		public string Icon { get; set; } = "";
	}

	public class CharacterRoleDefinition {
		public string RoleId { get; set; } = "";
		public string NameKey { get; set; } = "";
		public string DescriptionKey { get; set; } = "";
		public string Icon { get; set; } = "";
		public List<string> SkillIds { get; set; } = new();
	}

	public class SkillSettings {
		public int MinValue { get; set; }
		public int MaxValue { get; set; }
	}

	public class CharacterEntry {
		public string CharacterId { get; set; } = "";
		public List<string> NamePartKeys { get; set; } = new();
		public Dictionary<string, SkillSettings> Skills { get; set; } = new();
	}

	public class CountryCharacterPool {
		public string CountryId { get; set; } = "";
		public Dictionary<string, List<CharacterEntry>> Slots { get; set; } = new();
	}

	public class CharacterConfig {
		public List<CharacterSkillDefinition> Skills { get; set; } = new();
		public List<CharacterRoleDefinition> Roles { get; set; } = new();
		public List<CountryCharacterPool> CountryPools { get; set; } = new();

		public CharacterSkillDefinition? FindSkill(string skillId) {
			foreach (var s in Skills) {
				if (s.SkillId == skillId) return s;
			}
			return null;
		}

		public CharacterRoleDefinition? FindRole(string roleId) {
			foreach (var r in Roles) {
				if (r.RoleId == roleId) return r;
			}
			return null;
		}

		public CountryCharacterPool? FindPool(string countryId) {
			foreach (var p in CountryPools) {
				if (p.CountryId == countryId) return p;
			}
			return null;
		}
	}
}
