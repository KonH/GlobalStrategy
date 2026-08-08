using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ReceiveCardSystem {
		public static void Update(World world, ReadCommands<ReceiveCardCommand> commands) {
			foreach (ReceiveCardCommand command in commands.AsSpan()) {
				Process(world, command);
			}
		}

		static void Process(World world, ReceiveCardCommand command) {
			if (string.IsNullOrEmpty(command.OrgId) || command.ChoiceIndex < 0) {
				return;
			}

			int deckEntity = CountryCardDrawQuery.FindDeckEntity(world, command.OrgId);
			if (deckEntity < 0 || !world.Has<PendingCardDraw>(deckEntity)) {
				return;
			}

			int handSize = world.Get<CardHand>(deckEntity).HandSize;
			if (handSize <= 0 || CountryCardDrawQuery.CountHandCards(world, command.OrgId) >= handSize) {
				return;
			}
			HashSet<int> occupiedSlots = GetOccupiedSlots(world, command.OrgId, handSize);

			if (!CountryCardDrawQuery.TryGetCoherentChoices(
				world,
				command.OrgId,
				out IReadOnlyList<CountryCardDrawChoiceInfo> choices)
				|| command.ChoiceIndex >= choices.Count) {
				return;
			}
			int selectedEntity = choices[command.ChoiceIndex].Entity;

			int slotIndex = 0;
			while (occupiedSlots.Contains(slotIndex)) {
				slotIndex++;
			}
			if (slotIndex >= handSize) {
				return;
			}

			world.Add(selectedEntity, new CardInHand { SlotIndex = slotIndex });
			for (int i = 0; i < choices.Count; i++) {
				world.Remove<CardDrawChoice>(choices[i].Entity);
			}
			world.Remove<PendingCardDraw>(deckEntity);
		}

		static HashSet<int> GetOccupiedSlots(IReadOnlyWorld world, string orgId, int handSize) {
			var occupied = new HashSet<int>();
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId != orgId || owners[i].Value != CardOwnerKind.Country) {
						continue;
					}
					if (hands[i].SlotIndex >= 0 && hands[i].SlotIndex < handSize) {
						occupied.Add(hands[i].SlotIndex);
					}
				}
			}
			return occupied;
		}
	}
}
