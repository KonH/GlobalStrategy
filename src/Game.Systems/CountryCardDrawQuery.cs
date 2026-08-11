using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public readonly struct CountryCardDrawStatus {
		public int DeckEntity { get; }
		public int HandCount { get; }
		public int HandSize { get; }
		public bool HasPendingDraw { get; }
		public bool HasOfferMarkers { get; }
		public bool HasCoherentPendingDraw { get; }
		public bool CanStartDraw { get; }

		public CountryCardDrawStatus(
			int deckEntity,
			int handCount,
			int handSize,
			bool hasPendingDraw,
			bool hasOfferMarkers,
			bool hasCoherentPendingDraw,
			bool canStartDraw) {
			DeckEntity = deckEntity;
			HandCount = handCount;
			HandSize = handSize;
			HasPendingDraw = hasPendingDraw;
			HasOfferMarkers = hasOfferMarkers;
			HasCoherentPendingDraw = hasCoherentPendingDraw;
			CanStartDraw = canStartDraw;
		}
	}

	public readonly struct CountryCardDrawCandidate {
		public int Entity { get; }
		public int Weight { get; }

		public CountryCardDrawCandidate(int entity, int weight) {
			Entity = entity;
			Weight = weight;
		}
	}

	public readonly struct CountryCardDrawChoiceInfo {
		public int Entity { get; }
		public int ChoiceIndex { get; }

		public CountryCardDrawChoiceInfo(int entity, int choiceIndex) {
			Entity = entity;
			ChoiceIndex = choiceIndex;
		}
	}

	public static class CountryCardDrawQuery {
		public static bool TryGetStatus(
			IReadOnlyWorld world,
			ActionConfig config,
			string orgId,
			out CountryCardDrawStatus status) {
			int deckEntity = FindDeckEntity(world, orgId);
			if (deckEntity < 0) {
				status = default;
				return false;
			}

			int handSize = world.Get<CardHand>(deckEntity).HandSize;
			int handCount = CountHandCards(world, orgId);
			bool hasPendingDraw = world.Has<PendingCardDraw>(deckEntity);
			bool hasOfferMarkers = HasAnyOfferMarkers(world, orgId);
			bool hasCoherentPendingDraw = TryGetCoherentChoices(world, orgId, out _);
			bool canStartDraw = handSize > handCount
				&& !hasOfferMarkers
				&& GetDrawableCards(world, config, orgId).Count > 0;
			status = new CountryCardDrawStatus(
				deckEntity,
				handCount,
				handSize,
				hasPendingDraw,
				hasOfferMarkers,
				hasCoherentPendingDraw,
				canStartDraw);
			return true;
		}

		public static bool CanStartDraw(IReadOnlyWorld world, ActionConfig config, string orgId) {
			return TryGetStatus(world, config, orgId, out CountryCardDrawStatus status)
				&& status.CanStartDraw;
		}

		public static IReadOnlyList<CountryCardDrawCandidate> GetDrawableCards(
			IReadOnlyWorld world,
			ActionConfig config,
			string orgId) {
			var candidates = new List<CountryCardDrawCandidate>();
			if (string.IsNullOrEmpty(orgId)) {
				return candidates;
			}

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
			foreach (Archetype arch in world.GetMatchingArchetypes(required, excluded)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId != orgId || owners[i].Value != CardOwnerKind.Country) {
						continue;
					}
					ActionDefinition? definition = config.Find(actions[i].ActionId);
					if (definition == null || definition.DeckCopies <= 0) {
						continue;
					}
					int entity = arch.Entities[i];
					if (TargetsDestroyedCountry(world, entity)) {
						continue;
					}
					candidates.Add(new CountryCardDrawCandidate(entity, definition.DeckCopies));
				}
			}
			return candidates;
		}

		/// <summary>
		/// Country-targeted deck cards (relation / revenge) stay in the deck after a country is
		/// destroyed, but must not enter draw offers. Hand copies are left alone.
		/// </summary>
		static bool TargetsDestroyedCountry(IReadOnlyWorld world, int entity) {
			if (world.Has<CountryContext>(entity)
				&& CountryDestroySystem.IsCountryDestroyed(world, world.Get<CountryContext>(entity).CountryId)) {
				return true;
			}
			if (world.Has<RelationCardTarget>(entity)
				&& CountryDestroySystem.IsCountryDestroyed(
					world, world.Get<RelationCardTarget>(entity).TargetCountryId)) {
				return true;
			}
			return world.Has<RevengeCardTarget>(entity)
				&& CountryDestroySystem.IsCountryDestroyed(
					world, world.Get<RevengeCardTarget>(entity).TargetCountryId);
		}

		public static IReadOnlyList<CountryCardDrawChoiceInfo> GetChoices(IReadOnlyWorld world, string orgId) {
			var choices = new List<CountryCardDrawChoiceInfo>();
			if (string.IsNullOrEmpty(orgId)) {
				return choices;
			}

			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardDrawChoice>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				CardDrawChoice[] drawChoices = arch.GetColumn<CardDrawChoice>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId == orgId && owners[i].Value == CardOwnerKind.Country) {
						choices.Add(new CountryCardDrawChoiceInfo(arch.Entities[i], drawChoices[i].ChoiceIndex));
					}
				}
			}
			choices.Sort((left, right) => {
				int indexComparison = left.ChoiceIndex.CompareTo(right.ChoiceIndex);
				return indexComparison != 0 ? indexComparison : left.Entity.CompareTo(right.Entity);
			});
			return choices;
		}

		public static bool TryGetCoherentChoices(
			IReadOnlyWorld world,
			string orgId,
			out IReadOnlyList<CountryCardDrawChoiceInfo> choices) {
			choices = GetChoices(world, orgId);
			int deckEntity = FindDeckEntity(world, orgId);
			if (deckEntity < 0 || !world.Has<PendingCardDraw>(deckEntity)) {
				return false;
			}

			int optionCount = world.Get<PendingCardDraw>(deckEntity).OptionCount;
			if (optionCount < 1 || optionCount > 3 || choices.Count != optionCount) {
				return false;
			}

			for (int i = 0; i < choices.Count; i++) {
				if (choices[i].ChoiceIndex != i
					|| world.Has<CardInHand>(choices[i].Entity)
					|| world.Has<CardDiscard>(choices[i].Entity)) {
					return false;
				}
			}
			return true;
		}

		public static bool HasAnyOfferMarkers(IReadOnlyWorld world, string orgId) {
			if (string.IsNullOrEmpty(orgId)) {
				return false;
			}

			int[] pendingRequired = {
				TypeId<CardDeck>.Value,
				TypeId<PendingCardDraw>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(pendingRequired, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId == orgId) {
						return true;
					}
				}
			}

			int[] choiceRequired = {
				TypeId<OrgContext>.Value,
				TypeId<CardDrawChoice>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(choiceRequired, null)) {
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId == orgId) {
						return true;
					}
				}
			}
			return false;
		}

		public static int FindDeckEntity(IReadOnlyWorld world, string orgId) {
			if (string.IsNullOrEmpty(orgId)) {
				return -1;
			}

			int found = -1;
			int[] required = {
				TypeId<CardDeck>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardHand>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (decks[i].OrgId != orgId || owners[i].Value != CardOwnerKind.Country) {
						continue;
					}
					if (found >= 0) {
						return -1;
					}
					found = arch.Entities[i];
				}
			}
			return found;
		}

		public static int CountHandCards(IReadOnlyWorld world, string orgId) {
			int count = 0;
			int[] required = {
				TypeId<OrgContext>.Value,
				TypeId<CardOwnerType>.Value,
				TypeId<CardInHand>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				OrgContext[] organizations = arch.GetColumn<OrgContext>();
				CardOwnerType[] owners = arch.GetColumn<CardOwnerType>();
				for (int i = 0; i < arch.Count; i++) {
					if (organizations[i].OrgId == orgId && owners[i].Value == CardOwnerKind.Country) {
						count++;
					}
				}
			}
			return count;
		}
	}
}
