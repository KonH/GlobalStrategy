using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class CountryActionSystem {
		public struct ActionResult {
			public bool Executed;
			public bool Success;
		}

		public static void TickCooldowns(World world, DateTime currentTime) {
			int[] required = { TypeId<CountryActionCard>.Value, TypeId<ActionCooldown>.Value };
			var toRemove = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				ActionCooldown[] cooldowns = arch.GetColumn<ActionCooldown>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cooldowns[i].CooldownEndTime <= currentTime) {
						toRemove.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int e in toRemove) {
				world.Remove<ActionCooldown>(e);
			}
		}

		public static ActionResult ProcessPlayCountryAction(
			World world,
			PlayCountryActionCommand cmd,
			CountryActionConfig config,
			DateTime currentTime,
			Random rng) {
			var result = new ActionResult();

			var def = config.Find(cmd.ActionId);
			if (def == null) { return result; }

			// Compute org influence in country
			int orgInfluence = 0;
			int[] infReq = { TypeId<InfluenceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(infReq, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId == cmd.OrgId && effects[i].CountryId == cmd.CountryId) {
						orgInfluence += effects[i].Value;
					}
				}
			}

			// Eligibility check
			if (orgInfluence < def.InfluenceThreshold) { return result; }

			// Check and deduct gold
			int[] resReq = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			bool deducted = false;
			foreach (var arch in world.GetMatchingArchetypes(resReq, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != cmd.OrgId || resources[i].ResourceId != "gold") { continue; }
					if (resources[i].Value < def.GoldCost) { return result; }
					resources[i].Value -= def.GoldCost;
					deducted = true;
					break;
				}
				if (deducted) { break; }
			}
			if (!deducted) { return result; }

			result.Executed = true;

			// Remove InHand from played entity; record vacated slot
			int vacatedSlot = 0;
			int[] handReq = { TypeId<CountryActionCard>.Value, TypeId<InHand>.Value };
			int playedEntity = -1;
			foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
				CountryActionCard[] cards = arch.GetColumn<CountryActionCard>();
				InHand[] hands = arch.GetColumn<InHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OrgId == cmd.OrgId && cards[i].CountryId == cmd.CountryId &&
					    cards[i].ActionId == cmd.ActionId && cards[i].TargetCharacterId == cmd.TargetCharacterId) {
						vacatedSlot = hands[i].SlotIndex;
						playedEntity = arch.Entities[i];
						break;
					}
				}
				if (playedEntity >= 0) { break; }
			}
			if (playedEntity >= 0) {
				world.Remove<InHand>(playedEntity);
			}

			// Add ActionCooldown to ALL matching CountryActionCard entities
			int[] allCardReq = { TypeId<CountryActionCard>.Value };
			var toCooldown = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(allCardReq, null)) {
				CountryActionCard[] cards = arch.GetColumn<CountryActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OrgId == cmd.OrgId && cards[i].CountryId == cmd.CountryId &&
					    cards[i].ActionId == cmd.ActionId && cards[i].TargetCharacterId == cmd.TargetCharacterId) {
						toCooldown.Add(arch.Entities[i]);
					}
				}
			}
			var cooldownEnd = currentTime.AddMonths(def.CooldownMonths);
			foreach (int e in toCooldown) {
				if (!world.Has<ActionCooldown>(e)) {
					world.Add(e, new ActionCooldown { CooldownEndTime = cooldownEnd });
				} else {
					ref ActionCooldown cd = ref world.Get<ActionCooldown>(e);
					cd.CooldownEndTime = cooldownEnd;
				}
			}

			// Roll success
			float successRate = def.SuccessRateBase + (def.SuccessRateInfluenceDivisor > 0
				? orgInfluence / (float)def.SuccessRateInfluenceDivisor : 0f);
			if (successRate > 1f) { successRate = 1f; }
			result.Success = (float)rng.NextDouble() < successRate;

			if (result.Success) {
				// Add influence if pool not full
				if (def.InfluenceOnSuccess > 0) {
					int usedTotal = 0;
					foreach (var arch in world.GetMatchingArchetypes(infReq, null)) {
						InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
						int count = arch.Count;
						for (int i = 0; i < count; i++) {
							if (effects[i].CountryId == cmd.CountryId) {
								usedTotal += effects[i].Value;
							}
						}
					}
					if (usedTotal < 100) {
						int toAdd = Math.Min(def.InfluenceOnSuccess, 100 - usedTotal);
						int ie = world.Create();
						world.Add(ie, new InfluenceEffect {
							OrgId = cmd.OrgId,
							CountryId = cmd.CountryId,
							Value = toAdd,
							EffectId = $"country_action_{cmd.OrgId}_{cmd.ActionId}_{currentTime.Ticks}"
						});
					}
				}

				// Add opinion modifier to target character
				if (!string.IsNullOrEmpty(def.OpinionModifierSourceId) && !string.IsNullOrEmpty(cmd.TargetCharacterId)) {
					int[] charReq = { TypeId<Character>.Value, TypeId<CharacterOpinion>.Value };
					foreach (var arch in world.GetMatchingArchetypes(charReq, null)) {
						Character[] chars = arch.GetColumn<Character>();
						CharacterOpinion[] opinions = arch.GetColumn<CharacterOpinion>();
						int count = arch.Count;
						for (int i = 0; i < count; i++) {
							if (chars[i].CharacterId != cmd.TargetCharacterId) { continue; }
							ref CharacterOpinion opinion = ref opinions[i];
							if (opinion.ModifiersPerOrg == null) {
								opinion.ModifiersPerOrg = new Dictionary<string, List<OpinionModifier>>();
							}
							if (!opinion.ModifiersPerOrg.TryGetValue(cmd.OrgId, out var list)) {
								list = new List<OpinionModifier>();
								opinion.ModifiersPerOrg[cmd.OrgId] = list;
							}
							list.Add(new OpinionModifier {
								SourceId = def.OpinionModifierSourceId,
								Value = def.OpinionModifierValue,
								ChangeValue = def.OpinionModifierChangeValue
							});
							break;
						}
					}
				}
			}

			// Draw next card: recompute influence post-effect
			int newOrgInfluence = 0;
			foreach (var arch in world.GetMatchingArchetypes(infReq, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId == cmd.OrgId && effects[i].CountryId == cmd.CountryId) {
						newOrgInfluence += effects[i].Value;
					}
				}
			}

			// Find eligible deck cards (no InHand, no ActionCooldown, threshold met)
			int[] deckReq = { TypeId<CountryActionCard>.Value };
			int[] excludeInHand = { TypeId<InHand>.Value };

			var noHandIds = new HashSet<int>();
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHand)) {
				CountryActionCard[] cards = arch.GetColumn<CountryActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OrgId != cmd.OrgId || cards[i].CountryId != cmd.CountryId) { continue; }
					var d = config.Find(cards[i].ActionId);
					if (d == null || d.InfluenceThreshold > newOrgInfluence) { continue; }
					noHandIds.Add(arch.Entities[i]);
				}
			}
			int[] cooldownOnly = { TypeId<CountryActionCard>.Value, TypeId<ActionCooldown>.Value };
			var onCooldownIds = new HashSet<int>();
			foreach (var arch in world.GetMatchingArchetypes(cooldownOnly, null)) {
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					onCooldownIds.Add(arch.Entities[i]);
				}
			}
			var eligible = new List<int>();
			foreach (int id in noHandIds) {
				if (!onCooldownIds.Contains(id)) {
					eligible.Add(id);
				}
			}

			// Fisher-Yates shuffle
			for (int i = eligible.Count - 1; i > 0; i--) {
				int j = rng.Next(i + 1);
				(eligible[i], eligible[j]) = (eligible[j], eligible[i]);
			}

			// Fill hand up to MaxHandSize
			const int MaxHandSize = 3;
			var occupiedSlots = new HashSet<int>();
			foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
				CountryActionCard[] cards = arch.GetColumn<CountryActionCard>();
				InHand[] hands = arch.GetColumn<InHand>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OrgId == cmd.OrgId && cards[i].CountryId == cmd.CountryId) {
						occupiedSlots.Add(hands[i].SlotIndex);
					}
				}
			}
			int toDraw = Math.Min(MaxHandSize - occupiedSlots.Count, eligible.Count);
			int drawIdx = 0;
			for (int slot = 0; slot < MaxHandSize && drawIdx < toDraw; slot++) {
				if (!occupiedSlots.Contains(slot)) {
					world.Add(eligible[drawIdx], new InHand { SlotIndex = slot });
					drawIdx++;
				}
			}

			return result;
		}
	}
}
