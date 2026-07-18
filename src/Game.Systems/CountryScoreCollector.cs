using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public sealed class CountryScoreCollector : IResourceCollector {
		public const string Id = "country_score_formula";
		public const string CountryPopulationResourceId = "country_population";

		readonly double _coefficient;

		public CountryScoreCollector(double coefficient) {
			_coefficient = coefficient;
		}

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == CountryPopulationResourceId) {
						return resources[i].Value * _coefficient - currentValue;
					}
				}
			}
			return 0 - currentValue;
		}
	}
}
