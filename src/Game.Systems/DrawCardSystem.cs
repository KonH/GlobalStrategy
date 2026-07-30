using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class DrawCardSystem {
		public static void Update(World world, ActionConfig config, Random rng) {
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
				DrawCards(world, config, rng, orgId, countryId, count);
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
				DrawCards(world, config, rng, orgId, cid, count);
				toDestroy.Add(entity);
			}
			foreach (int e in toDestroy) { world.Destroy(e); }
		}

		static void DrawCards(World world, ActionConfig config, Random rng, string orgId, string countryId, int toDraw) {
			if (string.IsNullOrEmpty(countryId)) {
				DrawOrgCards(world, rng, orgId, toDraw);
			} else {
				DrawCountryCards(world, config, rng, orgId, countryId, toDraw);
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

		static void DrawCountryCards(World world, ActionConfig config, Random rng, string orgId, string countryId, int toDraw) {
			int orgControl = ControlQuery.GetOrgControlInCountry(world, orgId, countryId);
			int totalCountryControl = ControlQuery.GetTotalControlInCountry(world, countryId);
			string diplomacyCharId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, countryId, "diplomacy_advisor");
			double opinion = string.IsNullOrEmpty(diplomacyCharId) ? 0.0 : ResourceQuery.GetValue(world, diplomacyCharId, $"opinion_{orgId}");
			double hasSuitableTarget = CountryRelations.HasSuitableRelationTarget(world, countryId) ? 1.0 : 0.0;
			var ctx = new ExpressionContext {
				Control = orgControl,
				TotalCountryControl = totalCountryControl,
				Opinion = opinion,
				HasSuitableRelationTarget = hasSuitableTarget
			};

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
					ctx.RelationStillExists = 1.0;
					ctx.TargetRulerOrMilitaryOpinion = 0.0;
					ctx.NeitherSideAtWar = 1.0;
					if (world.Has<RelationCardTarget>(candidateEntity)) {
						var target = world.Get<RelationCardTarget>(candidateEntity);
						ctx.RelationStillExists = CountryRelations.GetRelation(world, countryId, target.TargetCountryId) == target.Kind ? 1.0 : 0.0;
						string rulerId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, target.TargetCountryId, "ruler");
						string militaryAdvisorId = CharacterQuery.GetTargetCharacterByCountryAndRole(world, target.TargetCountryId, "military_advisor");
						double rulerOpinion = string.IsNullOrEmpty(rulerId) ? 0.0 : ResourceQuery.GetValue(world, rulerId, $"opinion_{orgId}");
						double militaryAdvisorOpinion = string.IsNullOrEmpty(militaryAdvisorId) ? 0.0 : ResourceQuery.GetValue(world, militaryAdvisorId, $"opinion_{orgId}");
						ctx.TargetRulerOrMilitaryOpinion = Math.Max(rulerOpinion, militaryAdvisorOpinion);
						ctx.NeitherSideAtWar = !Wars.IsInWar(world, countryId) && !Wars.IsInWar(world, target.TargetCountryId) ? 1.0 : 0.0;
					}
					bool ok = true;
					foreach (var cond in def.Conditions) {
						if (ExpressionNode.Evaluate(cond, ctx) == 0.0) { ok = false; break; }
					}
					if (ok) { eligible.Add(candidateEntity); }
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

			int drawIdx = 0;
			for (int slot = 0; slot < maxHandSize && drawIdx < toDraw && drawIdx < eligible.Count; slot++) {
				if (!occupiedSlots.Contains(slot)) {
					world.Add(eligible[drawIdx], new CardInHand { SlotIndex = slot });
					drawIdx++;
				}
			}
		}
	}
}
