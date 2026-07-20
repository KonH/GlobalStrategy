using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public sealed class OrgScoreCollector : IResourceCollector {
		public const string Id = "org_score_formula";

		public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
			var controlByCountryId = new Dictionary<string, int>();
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId != ownerId) {
						continue;
					}
					controlByCountryId.TryGetValue(effects[i].CountryId, out int existing);
					controlByCountryId[effects[i].CountryId] = existing + effects[i].Value;
				}
			}

			double total = 0;
			foreach (var (countryId, control) in controlByCountryId) {
				double countryScore = ResourceQuery.GetValue(world, countryId, ResourceDefinitions.CountryScore);
				total += (control / 100.0) * countryScore;
			}
			return total - currentValue;
		}
	}
}
