using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class InfluenceSystem {
		public static void Update(World world, DateTime previousTime, DateTime currentTime) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			// Group influence effects by country → (org → totalValue)
			var byCountry = new Dictionary<string, Dictionary<string, int>>();
			int[] influenceRequired = { TypeId<InfluenceEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(influenceRequired, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ref InfluenceEffect fx = ref effects[i];
					if (!byCountry.TryGetValue(fx.CountryId, out var orgMap)) {
						orgMap = new Dictionary<string, int>();
						byCountry[fx.CountryId] = orgMap;
					}
					if (!orgMap.TryGetValue(fx.OrgId, out int existing)) {
						existing = 0;
					}
					orgMap[fx.OrgId] = existing + fx.Value;
				}
			}

			if (byCountry.Count == 0) {
				return;
			}

			int[] resourceRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value
			};

			foreach (var (countryId, orgMap) in byCountry) {
				double countryBaseIncome = ComputeBaseMonthlyGold(world, countryId);
				if (countryBaseIncome <= 0) {
					continue;
				}

				int totalInfluence = 0;
				foreach (var v in orgMap.Values) {
					totalInfluence += v;
				}
				if (totalInfluence <= 0) {
					continue;
				}

				double totalGain = 0;
				var orgGains = new List<(string OrgId, double Gain)>();
				foreach (var (orgId, orgInfluence) in orgMap) {
					double gain = Math.Round((orgInfluence / 100.0) * countryBaseIncome, 2);
					if (gain <= 0) {
						continue;
					}
					orgGains.Add((orgId, gain));
					totalGain += gain;
				}

				if (orgGains.Count == 0) {
					continue;
				}

				// Apply gains
				foreach (var (orgId, gain) in orgGains) {
					MutateResource(world, orgId, "gold", gain, resourceRequired);
				}
				MutateResource(world, countryId, "gold", -totalGain, resourceRequired);
			}
		}

		public static double ComputeBaseMonthlyGold(IReadOnlyWorld world, string countryId) {
			int[] effectRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};
			double total = 0;
			foreach (Archetype arch in world.GetMatchingArchetypes(effectRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != countryId) {
						continue;
					}
					if (links[i].ResourceId != "gold") {
						continue;
					}
					if (effects[i].PayType != PayType.Monthly) {
						continue;
					}
					if (effects[i].Value > 0) {
						total += effects[i].Value;
					}
				}
			}
			return total;
		}

		public static void ApplyChangeInfluence(World world, string orgId, string countryId, int delta) {
			int otherOrgsTotal = 0;
			int existingEntity = -1;
			int existingValue = 0;
			string permanentEffectId = $"permanent_{orgId}_{countryId}";
			int[] required = { TypeId<InfluenceEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int[] entities = arch.Entities;
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].CountryId != countryId) {
						continue;
					}
					if (effects[i].OrgId != orgId) {
						otherOrgsTotal += effects[i].Value;
					} else if (effects[i].EffectId == permanentEffectId) {
						existingEntity = entities[i];
						existingValue = effects[i].Value;
					}
				}
			}

			int newVal = Math.Max(0, Math.Min(existingValue + delta, 100 - otherOrgsTotal));

			if (existingEntity >= 0) {
				if (newVal == 0) {
					world.Destroy(existingEntity);
				} else {
					ref InfluenceEffect fx = ref world.Get<InfluenceEffect>(existingEntity);
					fx.Value = newVal;
				}
			} else if (newVal > 0) {
				int e = world.Create();
				world.Add(e, new InfluenceEffect {
					OrgId     = orgId,
					CountryId = countryId,
					Value     = newVal,
					EffectId  = permanentEffectId
				});
			}
		}

		static void MutateResource(
			World world, string ownerId, string resourceId, double delta, int[] resourceRequired) {
			foreach (Archetype arch in world.GetMatchingArchetypes(resourceRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						resources[i].Value += delta;
					}
				}
			}
		}
	}
}
