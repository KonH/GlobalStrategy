using ECS;

namespace GS.Game.Systems {
	public static class WartimeSkillQuery {
		public static double GetSkill(IReadOnlyWorld world, string countryId, string roleId, string skillId) {
			string characterId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, roleId);
			if (string.IsNullOrEmpty(characterId)) {
				return 0;
			}
			return ResourceQuery.GetValue(world, characterId, skillId);
		}
	}
}
