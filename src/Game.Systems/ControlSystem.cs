using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ControlSystem {
		public static void Update(
			World world, DateTime previousTime, DateTime currentTime,
			BaseIncomeSettings? baseIncomeSettings = null,
			ResourceQuery? resources = null) {
			bool isMonthBoundary = previousTime.Month != currentTime.Month
				|| previousTime.Year != currentTime.Year;
			if (!isMonthBoundary) {
				return;
			}

			var incomeSettings = baseIncomeSettings ?? new BaseIncomeSettings();

			// Group control effects by country → (org → totalValue)
			var byCountry = new Dictionary<string, Dictionary<string, int>>();
			int[] controlRequired = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(controlRequired, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ref ControlEffect fx = ref effects[i];
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

			if (resources == null) {
				throw new InvalidOperationException("ControlSystem.Update requires a ResourceQuery instance.");
			}

			foreach (var (countryId, orgMap) in byCountry) {
				double countryBaseIncome = ComputeBaseMonthlyGold(world, countryId, incomeSettings, resources);
				if (countryBaseIncome <= 0) {
					continue;
				}

				int totalControl = 0;
				foreach (var v in orgMap.Values) {
					totalControl += v;
				}
				if (totalControl <= 0) {
					continue;
				}

				double totalGain = 0;
				var orgGains = new List<(string OrgId, double Gain)>();
				foreach (var (orgId, orgControl) in orgMap) {
					double gain = Math.Round((orgControl / 100.0) * countryBaseIncome, 2);
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
					resources.ApplyDelta(world, orgId, ResourceDefinitions.Gold, gain, OwnerType.Org);
				}
				resources.ApplyDelta(world, countryId, ResourceDefinitions.Gold, -totalGain, OwnerType.Country);
			}
		}

		public static double ComputeBaseMonthlyGold(
			IReadOnlyWorld world, string countryId, BaseIncomeSettings? baseIncomeSettings, ResourceQuery resources) {
			var incomeSettings = baseIncomeSettings ?? new BaseIncomeSettings();
			int[] effectRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};
			double total = 0;
			bool sawLiveBaseIncome = false;
			foreach (Archetype arch in world.GetMatchingArchetypes(effectRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int[] entities = arch.Entities;
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != countryId) {
						continue;
					}
					if (links[i].ResourceId != ResourceDefinitions.Gold) {
						continue;
					}
					if (effects[i].PayType != PayType.Monthly) {
						continue;
					}
					bool useLiveFormula = effects[i].EffectId == BaseIncomeFormula.EffectId
						&& world.Has<ResourceCollector>(entities[i]);
					if (useLiveFormula) {
						if (!sawLiveBaseIncome) {
							total += BaseIncomeFormula.Compute(world, countryId, incomeSettings, resources).Total;
							sawLiveBaseIncome = true;
						}
						continue;
					}
					if (effects[i].Value > 0) {
						total += effects[i].Value;
					}
				}
			}
			return total;
		}

		public static void ApplyChangeControl(World world, string orgId, string countryId, int delta, int maxControlPool) {
			int otherOrgsTotal = 0;
			int existingEntity = -1;
			int existingValue = 0;
			string permanentEffectId = $"permanent_{orgId}_{countryId}";
			int[] required = { TypeId<ControlEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
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

			int newVal = Math.Max(0, Math.Min(existingValue + delta, maxControlPool - otherOrgsTotal));

			if (existingEntity >= 0) {
				if (newVal == 0) {
					world.Destroy(existingEntity);
				} else {
					ref ControlEffect fx = ref world.Get<ControlEffect>(existingEntity);
					fx.Value = newVal;
				}
			} else if (newVal > 0) {
				int e = world.Create();
				world.Add(e, new ControlEffect {
					OrgId     = orgId,
					CountryId = countryId,
					Value     = newVal,
					EffectId  = permanentEffectId
				});
			}
		}

	}
}
