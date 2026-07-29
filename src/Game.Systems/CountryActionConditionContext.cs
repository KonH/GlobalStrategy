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

			if (!string.IsNullOrEmpty(countryId)) {
				orgControl = ControlQuery.GetOrgControlInCountry(world, orgId, countryId);
				totalCountryControl = ControlQuery.GetTotalControlInCountry(world, countryId);
				hasSuitableRelationTarget = CountryRelations.HasSuitableRelationTarget(world, countryId) ? 1.0 : 0.0;
				isInWar = Wars.IsInWar(world, countryId) ? 1.0 : 0.0;

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
			if (cardEntity >= 0
				&& !string.IsNullOrEmpty(countryId)
				&& world.Has<RelationCardTarget>(cardEntity)) {
				RelationCardTarget target = world.Get<RelationCardTarget>(cardEntity);
				relationStillExists = CountryRelations.GetRelation(world, countryId, target.TargetCountryId) == target.Kind
					? 1.0
					: 0.0;
			}

			return new ExpressionContext {
				Control = orgControl,
				TotalCountryControl = totalCountryControl,
				Opinion = opinion,
				HasSuitableRelationTarget = hasSuitableRelationTarget,
				RelationStillExists = relationStillExists,
				IsInWar = isInWar
			};
		}
	}
}
