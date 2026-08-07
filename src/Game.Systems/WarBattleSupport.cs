using System;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

	namespace GS.Game.Systems {
	public static partial class WarBattleSystem {
		static WarParticipantKind Opposite(WarParticipantKind side) {
			return side == WarParticipantKind.Attacker
				? WarParticipantKind.Defender
				: WarParticipantKind.Attacker;
		}

		static double NextDouble(Random rng, double minimum, double maximum) {
			return minimum + rng.NextDouble() * (maximum - minimum);
		}

		static double RequireResource(
			ResourceQuery resources, IReadOnlyWorld world, string ownerId, string resourceId, string operation) {
			if (!resources.TryGetValue(world, ownerId, resourceId, out double value)) {
				throw new InvalidOperationException(
					$"Cannot continue {operation}: owner '{ownerId}' is missing resource '{resourceId}'.");
			}
			return value;
		}

		// Mutates synchronously (see Docs/Specs/26_07_29_16_war-progress/plan.md) and emits a
		// ResourceChange so VisualStateConverter can animate the same battle-caused resource
		// delta the DeductActionCostSystem/CreateActionEffectSystem card pipeline already does.
		static double RequireDelta(
			ResourceQuery resources, World world, string ownerId, string resourceId, double delta,
			double minimum, double maximum, string effectId, string operation) {
			if (!ResourceMutations.TryApplyClampedDelta(
				resources, world, ownerId, resourceId, delta, minimum, maximum, out double applied)) {
				throw new InvalidOperationException(
					$"Cannot continue {operation}: owner '{ownerId}' is missing resource '{resourceId}'.");
			}
			if (applied != 0) {
				int changeEntity = world.Create();
				world.Add(changeEntity, new ResourceChange {
					EffectId = effectId,
					ResourceId = resourceId,
					OwnerId = ownerId,
					Amount = applied
				});
			}
			return applied;
		}
	}
}
