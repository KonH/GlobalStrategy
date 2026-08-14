using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class TaskConditionContext {
		public static ExpressionContext Build(
			IReadOnlyWorld world,
			string orgId,
			ResourceQuery resources,
			CountryRelations relations,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			IReadOnlyDictionary<string, double>? triggers = null) {
			string hqCountryId = "";
			if (hqCountryByOrgId != null && !string.IsNullOrEmpty(orgId)) {
				hqCountryByOrgId.TryGetValue(orgId, out hqCountryId);
				hqCountryId ??= "";
			}

			int orgControl = 0;
			int totalCountryControl = 0;
			double isInWar = 0.0;
			double warProgress = 0.0;
			double warFree = 1.0;

			if (!string.IsNullOrEmpty(hqCountryId)) {
				orgControl = ControlQuery.GetOrgControlInCountry(world, orgId, hqCountryId);
				totalCountryControl = ControlQuery.GetTotalControlInCountry(world, hqCountryId);
				isInWar = Wars.IsInWar(world, hqCountryId) ? 1.0 : 0.0;
				warProgress = Wars.GetOwnWarProgress(world, resources, hqCountryId);
				warFree = Wars.IsWarFree(world, hqCountryId, hqCountryId) ? 1.0 : 0.0;
			}

			var relationValues = new Dictionary<string, double> {
				["none"] = 0.0,
				["friend"] = 0.0,
				["rival"] = 0.0
			};
			if (!string.IsNullOrEmpty(hqCountryId)) {
				foreach (string relationKind in new[] { "none", "friend", "rival" }) {
					relationValues[relationKind] = relations.MatchesCondition(world, hqCountryId, "", relationKind)
						? 1.0
						: 0.0;
				}
			}

			return new ExpressionContext {
				Control = orgControl,
				TotalCountryControl = totalCountryControl,
				Opinion = 0.0,
				CountryRelations = relationValues,
				Triggers = triggers ?? new Dictionary<string, double>(),
				IsInWar = isInWar,
				WarProgress = warProgress,
				TargetRulerOrMilitaryOpinion = 0.0,
				NeitherSideAtWar = 1.0,
				WarFree = warFree,
				RevengeEligible = 0.0
			};
		}
	}
}
