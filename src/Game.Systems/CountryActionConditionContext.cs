using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CountryActionConditionContext {
		public static ExpressionContext Build(
			IReadOnlyWorld world,
			ActionDefinition definition,
			string orgId,
			string countryId,
			int cardEntity = -1) {
			int orgControl = 0;
			int totalCountryControl = 0;
			double opinion = 0.0;
			double hasSuitableRelationTarget = 0.0;
			double isInWar = 0.0;
			double warProgress = 0.0;

			if (!string.IsNullOrEmpty(countryId)) {
				orgControl = ControlQuery.GetOrgControlInCountry(world, orgId, countryId);
				totalCountryControl = ControlQuery.GetTotalControlInCountry(world, countryId);
				hasSuitableRelationTarget = CountryRelations.HasSuitableRelationTarget(world, countryId) ? 1.0 : 0.0;
				isInWar = Wars.IsInWar(world, countryId) ? 1.0 : 0.0;
				warProgress = Wars.GetOwnWarProgress(world, countryId);

				if (!string.IsNullOrEmpty(definition.TargetRole)) {
					string characterId = CharacterQuery.GetTargetCharacterByCountryAndRole(
						world,
						countryId,
						definition.TargetRole);
					if (!string.IsNullOrEmpty(characterId)) {
						opinion = ResourceQuery.GetValue(world, characterId, $"opinion_{orgId}");
					}
				}
			}

			double relationStillExists = 1.0;
			double targetRulerOrMilitaryOpinion = 0.0;
			double neitherSideAtWar = 1.0;
			if (cardEntity >= 0
				&& !string.IsNullOrEmpty(countryId)
				&& world.Has<RelationCardTarget>(cardEntity)) {
				RelationCardTarget target = world.Get<RelationCardTarget>(cardEntity);
				relationStillExists = CountryRelations.GetRelation(world, countryId, target.TargetCountryId) == target.Kind
					? 1.0
					: 0.0;
				string rulerId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "ruler");
				string militaryAdvisorId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "military_advisor");
				double rulerOpinion = string.IsNullOrEmpty(rulerId) ? 0.0 : ResourceQuery.GetValue(world, rulerId, $"opinion_{orgId}");
				double militaryAdvisorOpinion = string.IsNullOrEmpty(militaryAdvisorId) ? 0.0 : ResourceQuery.GetValue(world, militaryAdvisorId, $"opinion_{orgId}");
				targetRulerOrMilitaryOpinion = System.Math.Max(rulerOpinion, militaryAdvisorOpinion);
				neitherSideAtWar = !Wars.IsInWar(world, countryId) && !Wars.IsInWar(world, target.TargetCountryId) ? 1.0 : 0.0;
			}

			return new ExpressionContext {
				Control = orgControl,
				TotalCountryControl = totalCountryControl,
				Opinion = opinion,
				HasSuitableRelationTarget = hasSuitableRelationTarget,
				RelationStillExists = relationStillExists,
				IsInWar = isInWar,
				WarProgress = warProgress,
				TargetRulerOrMilitaryOpinion = targetRulerOrMilitaryOpinion,
				NeitherSideAtWar = neitherSideAtWar
			};
		}
	}
}
