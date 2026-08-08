using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CountryActionConditionContext {
		public static ExpressionContext Build(
			IReadOnlyWorld world,
			ActionDefinition definition,
			string orgId,
			string countryId,
			ResourceQuery resources,
			CountryRelations relations,
			int cardEntity = -1,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null,
			ActionPlayabilityGateSet gateSet = ActionPlayabilityGateSet.All) {
			int orgControl = 0;
			int totalCountryControl = 0;
			double opinion = 0.0;
			double isInWar = 0.0;
			double warProgress = 0.0;
			double warFree = 1.0;
			double revengeEligible = 0.0;
			bool includeSoft = gateSet == ActionPlayabilityGateSet.All;

			string hqCountryId = "";
			if (hqCountryByOrgId != null && !string.IsNullOrEmpty(orgId)) {
				hqCountryByOrgId.TryGetValue(orgId, out hqCountryId);
				hqCountryId ??= "";
			}

			if (!string.IsNullOrEmpty(countryId)) {
				if (includeSoft) {
					orgControl = ControlQuery.GetOrgControlInCountry(world, orgId, countryId);
					totalCountryControl = ControlQuery.GetTotalControlInCountry(world, countryId);
				}
				isInWar = Wars.IsInWar(world, countryId) ? 1.0 : 0.0;
				warProgress = Wars.GetOwnWarProgress(world, resources, countryId);
				if (definition.ActionId == "declare_revenge_war" && cardEntity >= 0 && world.Has<RevengeCardTarget>(cardEntity)) {
					string targetCountryId = world.Get<RevengeCardTarget>(cardEntity).TargetCountryId;
					warFree = Wars.IsWarFree(world, countryId, targetCountryId) ? 1.0 : 0.0;
					revengeEligible = RevengeEligibilityQuery.IsEligible(world, countryId, targetCountryId) ? 1.0 : 0.0;
				} else {
					warFree = Wars.IsWarFree(world, countryId, hqCountryId) ? 1.0 : 0.0;
				}

				if (includeSoft && !string.IsNullOrEmpty(definition.TargetRole)) {
					string characterId = CharacterQuery.GetTargetCharacterByCountryAndRole(
						world,
						countryId,
						definition.TargetRole);
					if (!string.IsNullOrEmpty(characterId)) {
						opinion = resources.GetValue(world, characterId, $"opinion_{orgId}");
					}
				}
			}

			string relationTargetCountryId = cardEntity >= 0 && world.Has<RelationCardTarget>(cardEntity)
				? world.Get<RelationCardTarget>(cardEntity).TargetCountryId
				: "";
			var relationValues = new Dictionary<string, double>();
			foreach (string relationKind in new[] { "none", "friend", "rival" }) {
				relationValues[relationKind] = !string.IsNullOrEmpty(countryId)
					&& relations.MatchesCondition(world, countryId, relationTargetCountryId, relationKind)
					? 1.0
					: 0.0;
			}
			double targetRulerOrMilitaryOpinion = 0.0;
			double neitherSideAtWar = 1.0;
			if (cardEntity >= 0
				&& !string.IsNullOrEmpty(countryId)
				&& world.Has<RelationCardTarget>(cardEntity)) {
				RelationCardTarget target = world.Get<RelationCardTarget>(cardEntity);
				if (includeSoft) {
					string rulerId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "ruler");
					string militaryAdvisorId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "military_advisor");
					double rulerOpinion = string.IsNullOrEmpty(rulerId) ? 0.0 : resources.GetValue(world, rulerId, $"opinion_{orgId}");
					double militaryAdvisorOpinion = string.IsNullOrEmpty(militaryAdvisorId) ? 0.0 : resources.GetValue(world, militaryAdvisorId, $"opinion_{orgId}");
					targetRulerOrMilitaryOpinion = System.Math.Max(rulerOpinion, militaryAdvisorOpinion);
				}
				neitherSideAtWar = !Wars.IsInWar(world, countryId) && !Wars.IsInWar(world, target.TargetCountryId) ? 1.0 : 0.0;
			}

			return new ExpressionContext {
				Control = orgControl,
				TotalCountryControl = totalCountryControl,
				Opinion = opinion,
				CountryRelations = relationValues,
				IsInWar = isInWar,
				WarProgress = warProgress,
				TargetRulerOrMilitaryOpinion = targetRulerOrMilitaryOpinion,
				NeitherSideAtWar = neitherSideAtWar,
				WarFree = warFree,
				RevengeEligible = revengeEligible
			};
		}
	}
}
