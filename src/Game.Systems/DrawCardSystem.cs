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
			Random rng,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId = null) {
			int[] deckDrawReq = { TypeId<CardDeck>.Value, TypeId<CardDraw>.Value };
			var deckDraws = new List<(int entity, string orgId, string countryId, int count)>();
			foreach (var arch in world.GetMatchingArchetypes(deckDrawReq, null)) {
				CardDeck[] decks = arch.GetColumn<CardDeck>();
				CardDraw[] draws = arch.GetColumn<CardDraw>();
				int cnt = arch.Count;
				for (int i = 0; i < cnt; i++) {
					deckDraws.Add((arch.Entities[i], decks[i].OrgId, decks[i].CountryId, draws[i].Count));
				}
			}
			foreach (var (entity, orgId, countryId, count) in deckDraws) {
				DrawCards(world, config, rng, orgId, countryId, count, hqCountryByOrgId);
				world.Remove<CardDraw>(entity);
			}

			int[] orgDrawReq = { TypeId<OrgContext>.Value, TypeId<CardDraw>.Value };
			int[] excludeDeck = { TypeId<CardDeck>.Value };
			var syntheticDraws = new List<(int entity, string orgId, string countryId, int count)>();
			foreach (var arch in world.GetMatchingArchetypes(orgDrawReq, excludeDeck)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CardDraw[] draws = arch.GetColumn<CardDraw>();
				int cnt = arch.Count;
				for (int i = 0; i < cnt; i++) {
					syntheticDraws.Add((arch.Entities[i], orgs[i].OrgId, "", draws[i].Count));
				}
			}
			var toDestroy = new List<int>();
			foreach (var (entity, orgId, _, count) in syntheticDraws) {
				string cid = world.Has<CountryContext>(entity) ? world.Get<CountryContext>(entity).CountryId : "";
				DrawCards(world, config, rng, orgId, cid, count, hqCountryByOrgId);
				toDestroy.Add(entity);
			}
			foreach (int e in toDestroy) { world.Destroy(e); }
		}

		static void DrawCards(
			World world,
			ActionConfig config,
			Random rng,
			string orgId,
			string countryId,
			int toDraw,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId) {
			if (string.IsNullOrEmpty(countryId)) {
				DrawOrgCards(world, rng, orgId, toDraw);
			} else {
				DrawCountryCards(world, config, rng, orgId, countryId, toDraw, hqCountryByOrgId);
			}
		}

		static void DrawOrgCards(World world, Random rng, string orgId, int toDraw) {
			int[] deckReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value };
			int[] excludeInHandOrCountry = { TypeId<CardInHand>.Value, TypeId<CountryContext>.Value };
			var eligible = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHandOrCountry)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId == orgId) { eligible.Add(arch.Entities[i]); }
				}
			}

			for (int i = eligible.Count - 1; i > 0; i--) {
				int j = rng.Next(i + 1);
				(eligible[i], eligible[j]) = (eligible[j], eligible[i]);
			}

			int currentHand = CountOrgHand(world, orgId);
			int slot = currentHand;
			for (int k = 0; k < toDraw && k < eligible.Count; k++) {
				world.Add(eligible[k], new CardInHand { SlotIndex = slot++ });
			}
		}

		static int CountOrgHand(World world, string orgId) {
			int count = 0;
			int[] req = { TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
			int[] excludeCountry = { TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, excludeCountry)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				int c = arch.Count;
				for (int i = 0; i < c; i++) {
					if (orgs[i].OrgId == orgId) { count++; }
				}
			}
			return count;
		}

		static void DrawCountryCards(
			World world,
			ActionConfig config,
			Random rng,
			string orgId,
			string countryId,
			int toDraw,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId) {
			int[] deckReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value };
			int[] excludeInHand = { TypeId<CardInHand>.Value };
			var eligible = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHand)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId || countries[i].CountryId != countryId) { continue; }
					var def = config.Find(actions[i].ActionId);
					if (def == null) { continue; }
					int candidateEntity = arch.Entities[i];
					var ctx = CountryActionConditionContext.Build(
						world,
						def,
						orgId,
						countryId,
						candidateEntity,
						hqCountryByOrgId);
					bool ok = true;
					foreach (var cond in def.Conditions) {
						if (ExpressionNode.Evaluate(cond, ctx) == 0.0) { ok = false; break; }
					}
					if (!ok) { continue; }
					// Relation-synced cards exist as one entity per relation; DeckCopies is a draw
					// weight (0 = never drawn, 1 = standard, 2+ = increased chance). Static cards
					// already encode weight via multiple InitSystem entities, so add once each.
					int weight = world.Has<RelationCardTarget>(candidateEntity) ? def.DeckCopies : 1;
					for (int w = 0; w < weight; w++) {
						eligible.Add(candidateEntity);
					}
				}
			}

			for (int i = eligible.Count - 1; i > 0; i--) {
				int j = rng.Next(i + 1);
				(eligible[i], eligible[j]) = (eligible[j], eligible[i]);
			}

			const int maxHandSize = 3;
			var occupiedSlots = new HashSet<int>();
			int[] handReq = { TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				CardInHand[] hands = arch.GetColumn<CardInHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId == orgId && countries[i].CountryId == countryId) {
						occupiedSlots.Add(hands[i].SlotIndex);
					}
				}
			}

			var drawnThisPass = new HashSet<int>();
			int drawIdx = 0;
			int drawnCount = 0;
			for (int slot = 0; slot < maxHandSize && drawnCount < toDraw && drawIdx < eligible.Count; slot++) {
				if (occupiedSlots.Contains(slot)) { continue; }
				while (drawIdx < eligible.Count && drawnThisPass.Contains(eligible[drawIdx])) {
					drawIdx++;
				}
				if (drawIdx >= eligible.Count) { break; }
				int picked = eligible[drawIdx++];
				world.Add(picked, new CardInHand { SlotIndex = slot });
				drawnThisPass.Add(picked);
				drawnCount++;
			}
		}

		/// <summary>
		/// Debug cheat: move one matching deck card into hand, ignoring draw gates and hand-size caps.
		/// </summary>
		public static bool ForceDrawCard(World world, string orgId, string countryId, string actionId, string targetCountryId) {
			if (string.IsNullOrEmpty(orgId) || string.IsNullOrEmpty(actionId)) {
				return false;
			}

			int entity = FindMatchingDeckCard(world, orgId, countryId, actionId, targetCountryId);
			if (entity < 0 || world.Has<CardInHand>(entity)) {
				return false;
			}

			int slot = FindLowestFreeSlot(world, orgId, countryId);
			world.Add(entity, new CardInHand { SlotIndex = slot });
			return true;
		}

		static int FindMatchingDeckCard(World world, string orgId, string countryId, string actionId, string targetCountryId) {
			if (string.IsNullOrEmpty(countryId)) {
				int[] deckReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value };
				int[] excludeInHandOrCountry = { TypeId<CardInHand>.Value, TypeId<CountryContext>.Value };
				foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHandOrCountry)) {
					GameAction[] actions = arch.GetColumn<GameAction>();
					OrgContext[] orgs = arch.GetColumn<OrgContext>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (orgs[i].OrgId == orgId && actions[i].ActionId == actionId) {
							return arch.Entities[i];
						}
					}
				}
				return -1;
			}

			int[] countryDeckReq = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value };
			int[] excludeInHand = { TypeId<CardInHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryDeckReq, excludeInHand)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (orgs[i].OrgId != orgId || countries[i].CountryId != countryId || actions[i].ActionId != actionId) {
						continue;
					}
					int entity = arch.Entities[i];
					string entityTarget = world.Has<RelationCardTarget>(entity)
						? world.Get<RelationCardTarget>(entity).TargetCountryId
						: "";
					if (entityTarget == (targetCountryId ?? "")) {
						return entity;
					}
				}
			}
			return -1;
		}

		static int FindLowestFreeSlot(World world, string orgId, string countryId) {
			var occupied = new HashSet<int>();
			if (string.IsNullOrEmpty(countryId)) {
				int[] req = { TypeId<OrgContext>.Value, TypeId<CardInHand>.Value };
				int[] excludeCountry = { TypeId<CountryContext>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, excludeCountry)) {
					OrgContext[] orgs = arch.GetColumn<OrgContext>();
					CardInHand[] hands = arch.GetColumn<CardInHand>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (orgs[i].OrgId == orgId) {
							occupied.Add(hands[i].SlotIndex);
						}
					}
				}
			} else {
				int[] req = { TypeId<OrgContext>.Value, TypeId<CountryContext>.Value, TypeId<CardInHand>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, null)) {
					OrgContext[] orgs = arch.GetColumn<OrgContext>();
					CountryContext[] countries = arch.GetColumn<CountryContext>();
					CardInHand[] hands = arch.GetColumn<CardInHand>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (orgs[i].OrgId == orgId && countries[i].CountryId == countryId) {
							occupied.Add(hands[i].SlotIndex);
						}
					}
				}
			}

			int slot = 0;
			while (occupied.Contains(slot)) {
				slot++;
			}
			return slot;
		}
	}
}
