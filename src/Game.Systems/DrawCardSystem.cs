using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class DrawCardSystem {
		public static void Update(
			World world,
			ActionConfig config,
			Random rng) {
			int[] deckDrawReq = { TypeId<CardDeck>.Value, TypeId<CardOwnerType>.Value, TypeId<CardDraw>.Value };
			var deckDraws = new List<(int entity, string orgId, CardOwnerKind ownerKind, int count)>();
			foreach (var arch in world.GetMatchingArchetypes(deckDrawReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardDraw[] draws = arch.GetColumn<CardDraw>();
				for (int i = 0; i < arch.Count; i++) {
					deckDraws.Add((arch.Entities[i], decks[i].OrgId, owners[i].Value, draws[i].Count));
				}
			}
			foreach (var (entity, orgId, ownerKind, count) in deckDraws) {
				int drawn = DrawCards(world, config, rng, orgId, ownerKind, count);
				int remaining = count - drawn;
				if (remaining > 0) {
					world.Get<CardDraw>(entity).Count = remaining;
				} else {
					world.Remove<CardDraw>(entity);
				}
			}
		}

		static int DrawCards(
			World world,
			ActionConfig config,
			Random rng,
			string orgId,
			CardOwnerKind ownerKind,
			int toDraw) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value
			};
			int[] excluded = { TypeId<CardInHand>.Value, TypeId<CardDiscard>.Value };
			var candidates = new List<(int entity, int weight)>();
			foreach (var arch in world.GetMatchingArchetypes(required, excluded)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId != orgId || owners[i].Value != ownerKind) { continue; }
					ActionDefinition? definition = config.Find(actions[i].ActionId);
					if (definition == null || definition.DeckCopies <= 0) { continue; }
					candidates.Add((arch.Entities[i], definition.DeckCopies));
				}
			}

			var occupiedSlots = GetOccupiedSlots(world, orgId, ownerKind);
			int drawn = 0;
			for (; drawn < toDraw && candidates.Count > 0; drawn++) {
				int totalWeight = 0;
				foreach (var candidate in candidates) { totalWeight += candidate.weight; }
				if (totalWeight <= 0) { break; }

				int roll = rng.Next(totalWeight);
				int selectedIndex = 0;
				for (; selectedIndex < candidates.Count; selectedIndex++) {
					roll -= candidates[selectedIndex].weight;
					if (roll < 0) { break; }
				}

				int slotIndex = 0;
				while (occupiedSlots.Contains(slotIndex)) { slotIndex++; }
				occupiedSlots.Add(slotIndex);
				world.Add(candidates[selectedIndex].entity, new CardInHand { SlotIndex = slotIndex });
				candidates.RemoveAt(selectedIndex);
			}
			return drawn;
		}

		/// <summary>
		/// Debug cheat: move one matching deck card into hand, ignoring hand-size caps.
		/// </summary>
		public static bool ForceDrawCard(World world, string orgId, string countryId, string actionId, string targetCountryId) {
			if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(actionId)) { return false; }

			CardOwnerKind ownerKind = string.IsNullOrEmpty(countryId) ? CardOwnerKind.Org : CardOwnerKind.Country;
			int entity = FindMatchingDeckCard(world, orgId, ownerKind, countryId, actionId, targetCountryId);
			if (entity < 0) { return false; }

			int slot = FindLowestFreeSlot(world, orgId, ownerKind);
			world.Add(entity, new CardInHand { SlotIndex = slot });
			return true;
		}

		static int FindMatchingDeckCard(
			World world,
			string orgId,
			CardOwnerKind ownerKind,
			string selectedCountryId,
			string actionId,
			string targetCountryId) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value
			};
			int[] excluded = { TypeId<CardInHand>.Value, TypeId<CardDiscard>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, excluded)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId != orgId || owners[i].Value != ownerKind || actions[i].ActionId != actionId) { continue; }
					int entity = arch.Entities[i];
					string entityTarget = GetTargetCountryId(world, entity);
					if (entityTarget != (targetCountryId ?? "")) { continue; }
					if (!string.IsNullOrEmpty(entityTarget)
						&& world.TryGet<CountryContext>(entity, out var primary)
						&& primary.CountryId != selectedCountryId) {
						continue;
					}
					return entity;
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

		static HashSet<int> GetOccupiedSlots(IReadOnlyWorld world, string orgId, CardOwnerKind ownerKind) {
			var occupied = new HashSet<int>();
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrgId == orgId && owners[i].Value == ownerKind) {
						occupied.Add(hands[i].SlotIndex);
					}
				}
			}
			return occupied;
		}

		static int FindLowestFreeSlot(IReadOnlyWorld world, string orgId, CardOwnerKind ownerKind) {
			HashSet<int> occupied = GetOccupiedSlots(world, orgId, ownerKind);
			int slot = 0;
			while (occupied.Contains(slot)) { slot++; }
			return slot;
		}
	}
}
