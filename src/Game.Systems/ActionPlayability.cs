using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ActionPlayability {
		public static bool Evaluate(IReadOnlyWorld world, ActionConfig config, int entity, string actionId, string orgId, string? countryId) {
			var def = config.Find(actionId);
			if (def == null) { return false; }

			int orgControl = 0;
			int totalCountryControl = 0;

			double opinion = 0.0;
			double hasSuitableTarget = 0.0;
			double isInWar = 0.0;
			double warProgress = 0.0;
			if (!string.IsNullOrEmpty(countryId)) {
				orgControl = ControlQuery.GetOrgControlInCountry(world, orgId, countryId);
				totalCountryControl = ControlQuery.GetTotalControlInCountry(world, countryId);
				string advisorCharId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, def.TargetRole);
				opinion = string.IsNullOrEmpty(advisorCharId) ? 0.0 : ResourceQuery.GetValue(world, advisorCharId, $"opinion_{orgId}");
				hasSuitableTarget = CountryRelations.HasSuitableRelationTarget(world, countryId) ? 1.0 : 0.0;
				isInWar = Wars.IsInWar(world, countryId) ? 1.0 : 0.0;
				warProgress = Wars.GetOwnWarProgress(world, countryId);
			}
			double relationStillExists = 1.0;
			double targetRulerOrMilitaryOpinion = 0.0;
			double neitherSideAtWar = 1.0;
			if (entity >= 0 && !string.IsNullOrEmpty(countryId) && world.Has<RelationCardTarget>(entity)) {
				var target = world.Get<RelationCardTarget>(entity);
				relationStillExists = CountryRelations.GetRelation(world, countryId, target.TargetCountryId) == target.Kind ? 1.0 : 0.0;
				string rulerId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "ruler");
				string militaryAdvisorId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "military_advisor");
				double rulerOpinion = string.IsNullOrEmpty(rulerId) ? 0.0 : ResourceQuery.GetValue(world, rulerId, $"opinion_{orgId}");
				double militaryAdvisorOpinion = string.IsNullOrEmpty(militaryAdvisorId) ? 0.0 : ResourceQuery.GetValue(world, militaryAdvisorId, $"opinion_{orgId}");
				targetRulerOrMilitaryOpinion = Math.Max(rulerOpinion, militaryAdvisorOpinion);
				neitherSideAtWar = !Wars.IsInWar(world, countryId) && !Wars.IsInWar(world, target.TargetCountryId) ? 1.0 : 0.0;
			}
			var ctx = new ExpressionContext {
				Control = orgControl,
				TotalCountryControl = totalCountryControl,
				Opinion = opinion,
				HasSuitableRelationTarget = hasSuitableTarget,
				RelationStillExists = relationStillExists,
				IsInWar = isInWar,
				WarProgress = warProgress,
				TargetRulerOrMilitaryOpinion = targetRulerOrMilitaryOpinion,
				NeitherSideAtWar = neitherSideAtWar
			};

			foreach (var cond in def.Conditions) {
				if (ExpressionNode.Evaluate(cond, ctx) == 0.0) { return false; }
			}

			if (!CanAfford(world, orgId, def.Cost)) { return false; }

			return true;
		}

		public static bool CanAfford(IReadOnlyWorld world, string orgId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				int entity = FindResourceEntity(world, orgId, cost.ResourceId);
				if (entity < 0 || world.Get<Resource>(entity).Value < cost.Amount) { return false; }
			}
			return true;
		}

		public static int FindResourceEntity(IReadOnlyWorld world, string ownerId, string resourceId) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId && resources[i].ResourceId == resourceId) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}
	}
}
