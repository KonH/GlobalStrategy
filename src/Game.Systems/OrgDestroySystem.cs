using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class OrgDestroySystem {
		static readonly CardOwnerKind[] s_ownerKinds = {
			CardOwnerKind.Org,
			CardOwnerKind.Country
		};

		public static int EvaluateAll(
			World world,
			ActionConfig actionConfig,
			EffectConfig effectConfig,
			ResourceQuery resources,
			CountryRelations relations,
			GameSettings settings,
			int maxControlPool) {
			var orgEntities = new List<int>();
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				for (int i = 0; i < archetype.Count; i++) {
					orgEntities.Add(archetype.Entities[i]);
				}
			}

			int newlyDestroyed = 0;
			foreach (int orgEntity in orgEntities) {
				if (TryDestroyIfConditionsMet(
					world, orgEntity, actionConfig, effectConfig, resources, relations, settings, maxControlPool)) {
					newlyDestroyed++;
				}
			}
			return newlyDestroyed;
		}

		public static bool TryDestroyIfConditionsMet(
			World world,
			int orgEntity,
			ActionConfig actionConfig,
			EffectConfig effectConfig,
			ResourceQuery resources,
			CountryRelations relations,
			GameSettings settings,
			int maxControlPool) {
			if (!world.Has<Organization>(orgEntity) || world.Has<IsOrgDestroyed>(orgEntity)) {
				return false;
			}

			string orgId = world.Get<Organization>(orgEntity).OrganizationId;
			if (OrgMetrics.GetTotalControl(world, orgId) > 0) {
				return false;
			}

			HashSet<string> availableCountryIds = GameCompletionSystem.GetAvailableCountryIds(world);
			foreach (CardOwnerKind ownerKind in s_ownerKinds) {
				IReadOnlyList<int> cards = OrgDestroyHandQuery.GetHandCardEntities(world, orgId, ownerKind);
				foreach (int cardEntity in cards) {
					ActionDefinition? definition = world.Has<GameAction>(cardEntity)
						? actionConfig.Find(world.Get<GameAction>(cardEntity).ActionId)
						: null;
					if (ActionEffectClassifier.RaisesControl(definition, effectConfig)) {
						return false;
					}
				}

				if (OrgDestroyHandQuery.TryGetHandSize(
					world, orgId, ownerKind, out int handCount, out int handSize)
					&& handCount < handSize) {
					return false;
				}

				foreach (int cardEntity in cards) {
					if (IsPlayableIgnoringCooldown(
						world, actionConfig, resources, relations, orgId, ownerKind,
						cardEntity, availableCountryIds, maxControlPool)) {
						return false;
					}
				}
			}

			if (OrgMetrics.GetGold(world, orgId) >= settings.DiscardGoldCost) {
				return false;
			}

			ApplyDestruction(world, orgEntity, orgId);
			return true;
		}

		// Debug-only: applies the same destruction outcome as TryDestroyIfConditionsMet
		// immediately, skipping the gold/hand/control preconditions. Used to simulate an
		// org's destruction on demand from the debug menu.
		public static bool ForceDestroy(World world, string orgId) {
			if (string.IsNullOrEmpty(orgId)) {
				return false;
			}
			int orgEntity = FindOrgEntity(world, orgId);
			if (orgEntity < 0 || world.Has<IsOrgDestroyed>(orgEntity)) {
				return false;
			}
			ApplyDestruction(world, orgEntity, orgId);
			return true;
		}

		static void ApplyDestruction(World world, int orgEntity, string orgId) {
			world.Add(orgEntity, new IsOrgDestroyed());
			ControlQuery.DestroyAllControlForOrg(world, orgId);
			if (world.Has<OrganizationGameOutcome>(orgEntity)) {
				world.Get<OrganizationGameOutcome>(orgEntity).Result = OrganizationGameResult.Loser;
			}
			int eventEntity = world.Create();
			world.Add(eventEntity, new OrgDestroyedApplied { OrganizationId = orgId });
		}

		static int FindOrgEntity(IReadOnlyWorld world, string orgId) {
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				for (int i = 0; i < archetype.Count; i++) {
					if (organizations[i].OrganizationId == orgId) {
						return archetype.Entities[i];
					}
				}
			}
			return -1;
		}

		public static bool IsOrgDestroyed(IReadOnlyWorld world, string orgId) {
			if (string.IsNullOrEmpty(orgId)) {
				return false;
			}
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				for (int i = 0; i < archetype.Count; i++) {
					if (organizations[i].OrganizationId == orgId) {
						return world.Has<IsOrgDestroyed>(archetype.Entities[i]);
					}
				}
			}
			return false;
		}

		static bool IsPlayableIgnoringCooldown(
			IReadOnlyWorld world,
			ActionConfig actionConfig,
			ResourceQuery resources,
			CountryRelations relations,
			string orgId,
			CardOwnerKind ownerKind,
			int cardEntity,
			IReadOnlyCollection<string> availableCountryIds,
			int maxControlPool) {
			if (!world.Has<GameAction>(cardEntity)) {
				return false;
			}
			string actionId = world.Get<GameAction>(cardEntity).ActionId;
			if (ownerKind == CardOwnerKind.Org) {
				return HasNoNonCooldownFailures(ActionPlayability.Evaluate(
					world, actionConfig, cardEntity, actionId, orgId, null,
					resources, relations, currentTime: default, maxControlPool: maxControlPool));
			}

			foreach (string countryId in availableCountryIds) {
				if (HasNoNonCooldownFailures(ActionPlayability.Evaluate(
					world, actionConfig, cardEntity, actionId, orgId, countryId,
					resources, relations, currentTime: default, maxControlPool: maxControlPool))) {
					return true;
				}
			}
			return false;
		}

		static bool HasNoNonCooldownFailures(ActionPlayabilityResult result) {
			foreach (ActionConditionDebugEntry entry in result.Entries) {
				if (!entry.Passed && entry.ReasonCode != "on_cooldown") {
					return false;
				}
			}
			return true;
		}
	}
}
