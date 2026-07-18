using ECS;

namespace GS.Game.Systems {
	public static class CountryScoreSystem {
		public static double GetScore(IReadOnlyWorld world, string countryId) {
			return ResourceQuery.GetValue(world, countryId, CountryScoreCollector.ResourceId);
		}
	}
}
