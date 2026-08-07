using ECS;

namespace GS.Game.Systems {
	public static class WartimeSkillQuery {
		public static double GetSkill(
			IReadOnlyWorld world, string countryId, string roleId, string skillId, ResourceQuery resources) {
			string characterId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, roleId);
			if (string.IsNullOrEmpty(characterId)) {
				return 0;
			}
			return resources.GetValue(world, characterId, skillId);
		}
	}
}
