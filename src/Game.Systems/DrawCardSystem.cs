using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class DrawCardSystem {
		public static void Update(
			World world,
			ActionConfig config,
			Random rng,
			ReadCommands<DrawCardsCommand> commands,
			IReadOnlyList<DiscardCardResult> discardResults) {
			ScheduleOrgRefills(world);
			RefillOrgHands(world, config, rng);

			foreach (DrawCardsCommand command in commands.AsSpan()) {
				TryCreateOffer(world, config, rng, command.OrgId);
			}

			for (int i = 0; i < discardResults.Count; i++) {
				DiscardCardResult result = discardResults[i];
				if (result.Outcome == DiscardCardOutcome.Succeeded) {
					TryCreateOffer(world, config, rng, result.Command.OrgId);
				}
			}
		}

		static void ScheduleOrgRefills(World world) {
			var organizations = new HashSet<string>(StringComparer.Ordinal);
			int[] discardedRequired = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardDiscard>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(discardedRequired, null)) {
				OrgContext[] organizationContexts = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].Value == CardOwnerKind.Org) {
						organizations.Add(organizationContexts[i].OrgId);
					}
				}
			}

			foreach (string orgId in organizations) {
				ScheduleOrgRefill(world, orgId);
			}
		}

		static void ScheduleOrgRefill(World world, string orgId) {
			int[] deckRequired = {
				TypeId<CardDeck>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardHand>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(deckRequired, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId != orgId || owners[i].Value != CardOwnerKind.Org) {
						continue;
					}
					int missing = hands[i].HandSize - GetOccupiedSlots(world, orgId, CardOwnerKind.Org).Count;
					if (missing <= 0) {
						return;
					}
					int deckEntity = arch.Entities[i];
					if (world.Has<OrgCardRefill>(deckEntity)) {
						ref OrgCardRefill refill = ref world.Get<OrgCardRefill>(deckEntity);
						refill.Count = Math.Max(refill.Count, missing);
					} else {
						world.Add(deckEntity, new OrgCardRefill { Count = missing });
					}
					return;
				}
			}
		}

		static void RefillOrgHands(World world, ActionConfig config, Random rng) {
			int[] deckRequired = {
				TypeId<CardDeck>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardHand>.Value,
				TypeId<OrgCardRefill>.Value
			};
			var refills = new List<(int Entity, string OrgId, int Count, int HandSize)>();
			foreach (Archetype arch in world.GetMatchingArchetypes(deckRequired, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardHand[] hands = arch.GetColumn<CardHand>();
				OrgCardRefill[] refillComponents = arch.GetColumn<OrgCardRefill>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].Value == CardOwnerKind.Org) {
						refills.Add((arch.Entities[i], decks[i].OrgId, refillComponents[i].Count, hands[i].HandSize));
					}
				}
			}

			foreach (var refill in refills) {
				int capacity = refill.HandSize - GetOccupiedSlots(world, refill.OrgId, CardOwnerKind.Org).Count;
				if (capacity <= 0) {
					world.Remove<OrgCardRefill>(refill.Entity);
					continue;
				}
				int requested = Math.Min(refill.Count, capacity);
				int drawn = DrawOrgCards(world, config, rng, refill.OrgId, requested);
				int remaining = refill.Count - drawn;
				if (remaining > 0) {
					world.Get<OrgCardRefill>(refill.Entity).Count = remaining;
				} else {
					world.Remove<OrgCardRefill>(refill.Entity);
				}
			}
		}

		static int DrawOrgCards(World world, ActionConfig config, Random rng, string orgId, int toDraw) {
			int[] required = {
				TypeId<GameAction>.Value,
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value
			};
			int[] excluded = { TypeId<CardInHand>.Value, TypeId<CardDiscard>.Value };
			var candidates = new List<(int Entity, int Weight)>();
			foreach (Archetype arch in world.GetMatchingArchetypes(required, excluded)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId != orgId || owners[i].Value != CardOwnerKind.Org) {
						continue;
					}
					ActionDefinition? definition = config.Find(actions[i].ActionId);
					if (definition == null || definition.DeckCopies <= 0) {
						continue;
					}
					candidates.Add((arch.Entities[i], definition.DeckCopies));
				}
			}

			HashSet<int> occupiedSlots = GetOccupiedSlots(world, orgId, CardOwnerKind.Org);
			int drawn = 0;
			for (; drawn < toDraw && candidates.Count > 0; drawn++) {
				int totalWeight = 0;
				foreach (var candidate in candidates) {
					totalWeight += candidate.Weight;
				}
				int roll = rng.Next(totalWeight);
				int selectedIndex = 0;
				for (; selectedIndex < candidates.Count; selectedIndex++) {
					roll -= candidates[selectedIndex].Weight;
					if (roll < 0) {
						break;
					}
				}

				int slotIndex = 0;
				while (occupiedSlots.Contains(slotIndex)) {
					slotIndex++;
				}
				occupiedSlots.Add(slotIndex);
				world.Add(candidates[selectedIndex].Entity, new CardInHand { SlotIndex = slotIndex });
				candidates.RemoveAt(selectedIndex);
			}
			return drawn;
		}

		static bool TryCreateOffer(
			World world,
			ActionConfig config,
			Random rng,
			string orgId) {
			if (!CountryCardDrawQuery.TryGetStatus(world, config, orgId, out CountryCardDrawStatus status)
				|| !status.CanStartDraw) {
				return false;
			}

			var candidates = new List<CountryCardDrawCandidate>(
				CountryCardDrawQuery.GetDrawableCards(world, config, orgId));
			int optionCount = Math.Min(3, candidates.Count);
			for (int choiceIndex = 0; choiceIndex < optionCount; choiceIndex++) {
				int totalWeight = 0;
				foreach (CountryCardDrawCandidate candidate in candidates) {
					totalWeight += candidate.Weight;
				}

				int roll = rng.Next(totalWeight);
				int selectedIndex = 0;
				for (; selectedIndex < candidates.Count; selectedIndex++) {
					roll -= candidates[selectedIndex].Weight;
					if (roll < 0) { break; }
				}

				world.Add(candidates[selectedIndex].Entity, new CardDrawChoice { ChoiceIndex = choiceIndex });
				candidates.RemoveAt(selectedIndex);
			}

			world.Add(status.DeckEntity, new PendingCardDraw { OptionCount = optionCount });
			return true;
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
			int[] excluded = {
				TypeId<CardInHand>.Value,
				TypeId<CardDrawChoice>.Value,
				TypeId<CardDiscard>.Value
			};
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
