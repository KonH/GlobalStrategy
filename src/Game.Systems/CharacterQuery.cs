using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CharacterQuery {
		public static string GetTargetCharacterByCountryAndRole(IReadOnlyWorld world, string countryId, string targetRole) {
			if (string.IsNullOrEmpty(targetRole)) { return ""; }
			int[] req = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (chars[i].CountryId == countryId && chars[i].RoleId == targetRole) {
						return chars[i].CharacterId;
					}
				}
			}
			return "";
		}
	}
}
