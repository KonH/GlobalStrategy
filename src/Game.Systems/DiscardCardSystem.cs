using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public enum DiscardCardOutcome {
		Succeeded,
		InvalidCommand,
		CardNotFound,
		InsufficientGold
	}

	public readonly struct DiscardCardResult {
		public DiscardCardCommand Command { get; }
		public DiscardCardOutcome Outcome { get; }

		public DiscardCardResult(DiscardCardCommand command, DiscardCardOutcome outcome) {
			Command = command;
			Outcome = outcome;
		}
	}

	public static class DiscardCardSystem {
		public static IReadOnlyList<DiscardCardResult> Update(
			World world,
			ReadCommands<DiscardCardCommand> commands,
			double goldCost,
			ResourceQuery resources) {
			if (goldCost < 0) {
				throw new ArgumentOutOfRangeException(nameof(goldCost), goldCost, "The discard gold cost cannot be negative.");
			}
			if (commands.Count == 0) {
				return Array.Empty<DiscardCardResult>();
			}

			var results = new List<DiscardCardResult>(commands.Count);
			foreach (DiscardCardCommand command in commands.AsSpan()) {
				results.Add(new DiscardCardResult(command, Process(world, command, goldCost, resources)));
			}
			return results;
		}

		static DiscardCardOutcome Process(World world, DiscardCardCommand command, double goldCost, ResourceQuery resources) {
			if (string.IsNullOrEmpty(command.OrgId)
				|| string.IsNullOrEmpty(command.CountryId)
				|| string.IsNullOrEmpty(command.ActionId)
				|| command.SlotIndex < 0) {
				return DiscardCardOutcome.InvalidCommand;
			}

			int cardEntity = FindCard(world, command);
			if (cardEntity < 0) {
				return DiscardCardOutcome.CardNotFound;
			}

			int goldEntity = resources.FindEntity(world, command.OrgId, ResourceDefinitions.Gold);
			if (goldEntity < 0 || world.Get<Resource>(goldEntity).Value < goldCost) {
				return DiscardCardOutcome.InsufficientGold;
			}

			ref Resource gold = ref world.Get<Resource>(goldEntity);
			gold.Value -= goldCost;
			resources.NotifyValue(command.OrgId, ResourceDefinitions.Gold, gold.Value, goldEntity);
			AddResourceChange(world, command, goldCost);

			world.Remove<CardInHand>(cardEntity);
			world.Add(cardEntity, new CardDiscard());
			return DiscardCardOutcome.Succeeded;
		}

		static int FindCard(IReadOnlyWorld world, DiscardCardCommand command) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId != command.OrgId
						|| owners[i].Value != CardOwnerKind.Country
						|| actions[i].ActionId != command.ActionId
						|| hands[i].SlotIndex != command.SlotIndex) {
						continue;
					}

					int entity = arch.Entities[i];
					if (GetTargetCountryId(world, entity) == (command.TargetCountryId ?? "")) {
						return entity;
					}
				}
			}
			return -1;
		}

		static string GetTargetCountryId(IReadOnlyWorld world, int entity) {
			if (world.Has<RelationCardTarget>(entity)) {
				return world.Get<RelationCardTarget>(entity).TargetCountryId;
			}
			return world.Has<RevengeCardTarget>(entity)
				? world.Get<RevengeCardTarget>(entity).TargetCountryId
				: "";
		}

		static void AddResourceChange(World world, DiscardCardCommand command, double goldCost) {
			int entity = world.Create();
			world.Add(entity, new ResourceChange {
				EffectId = $"discard_{command.OrgId}_{command.ActionId}_{command.SlotIndex}_{ResourceDefinitions.Gold}",
				ResourceId = ResourceDefinitions.Gold,
				OwnerId = command.OrgId,
				Amount = -goldCost
			});
		}
	}
}
