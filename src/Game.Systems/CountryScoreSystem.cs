using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class CountryScoreSystem {
		public static double GetScore(IReadOnlyWorld world, string countryId) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == countryId && resources[i].ResourceId == "country_score") {
						return resources[i].Value;
					}
				}
			}
			return 0;
		}
	}
}
