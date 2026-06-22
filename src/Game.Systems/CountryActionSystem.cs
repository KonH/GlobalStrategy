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
			public int InfluenceAdded;
			public string OpinionTargetCharId;
			public int OpinionDelta;
		}

		public static ActionResult ProcessPlayCountryAction(
			World world,
			PlayCountryActionCommand cmd,
			ActionConfig config,
			EffectConfig effectConfig,
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

			// Eligibility check via conditions
			var condCtx = new ExpressionContext { Influence = orgInfluence };
			foreach (var cond in def.Conditions) {
				if (ExpressionNode.Evaluate(cond, condCtx) == 0.0) { return result; }
			}

			// Check and deduct cost
			if (!CanAffordCost(world, cmd.OrgId, def.Cost)) { return result; }
			DeductCost(world, cmd.OrgId, def.Cost);

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

			// Roll success
			float successRate = (float)ExpressionNode.Evaluate(def.SuccessRateNode, new ExpressionContext { Influence = orgInfluence });
			if (successRate > 1f) { successRate = 1f; }
			result.Success = (float)rng.NextDouble() < successRate;

			if (result.Success) {
				// Apply influence effects
				foreach (var effectId in def.EffectIds) {
					var effectEntry = effectConfig.Find(effectId);
					if (effectEntry is InfluenceChangeEffectParams influenceParams && influenceParams.Amount > 0) {
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
							int toAdd = Math.Min(influenceParams.Amount, 100 - usedTotal);
							int ie = world.Create();
							world.Add(ie, new InfluenceEffect {
								OrgId = cmd.OrgId,
								CountryId = cmd.CountryId,
								Value = toAdd,
								EffectId = $"country_action_{cmd.OrgId}_{cmd.ActionId}_{currentTime.Ticks}"
							});
							result.InfluenceAdded += toAdd;
						}
					}
				}

				// Apply opinion modifier effects
				foreach (var effectId in def.EffectIds) {
					var effectEntry = effectConfig.Find(effectId);
					if (effectEntry is OpinionModifierEffectParams opinionParams) {
						if (!string.IsNullOrEmpty(opinionParams.SourceId) && !string.IsNullOrEmpty(cmd.TargetCharacterId)) {
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
										SourceId = opinionParams.SourceId,
										Value = opinionParams.InitialValue,
										ChangeValue = -opinionParams.DecayPerMonth
									});
									result.OpinionTargetCharId = cmd.TargetCharacterId;
									result.OpinionDelta = opinionParams.InitialValue;
									break;
								}
							}
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

			// Find eligible deck cards (no InHand, conditions met)
			int[] deckReq = { TypeId<CountryActionCard>.Value };
			int[] excludeInHand = { TypeId<InHand>.Value };

			var noHandIds = new HashSet<int>();
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeInHand)) {
				CountryActionCard[] cards = arch.GetColumn<CountryActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OrgId != cmd.OrgId || cards[i].CountryId != cmd.CountryId) { continue; }
					var d = config.Find(cards[i].ActionId);
					if (d == null) { continue; }
					bool eligible = true;
					var drawCtx = new ExpressionContext { Influence = newOrgInfluence };
					foreach (var cond in d.Conditions) {
						if (ExpressionNode.Evaluate(cond, drawCtx) == 0.0) {
							eligible = false;
							break;
						}
					}
					if (!eligible) { continue; }
					noHandIds.Add(arch.Entities[i]);
				}
			}
			var eligible2 = new List<int>(noHandIds);

			// Fisher-Yates shuffle
			for (int i = eligible2.Count - 1; i > 0; i--) {
				int j = rng.Next(i + 1);
				(eligible2[i], eligible2[j]) = (eligible2[j], eligible2[i]);
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
			int toDraw = Math.Min(MaxHandSize - occupiedSlots.Count, eligible2.Count);
			int drawIdx = 0;
			for (int slot = 0; slot < MaxHandSize && drawIdx < toDraw; slot++) {
				if (!occupiedSlots.Contains(slot)) {
					world.Add(eligible2[drawIdx], new InHand { SlotIndex = slot });
					drawIdx++;
				}
			}

			return result;
		}

		static bool CanAffordCost(World world, string ownerId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
				bool found = false;
				foreach (var arch in world.GetMatchingArchetypes(req, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId != ownerId || resources[i].ResourceId != cost.ResourceId) { continue; }
						if (resources[i].Value < cost.Amount) { return false; }
						found = true;
						break;
					}
					if (found) { break; }
				}
				if (!found && cost.Amount > 0) { return false; }
			}
			return true;
		}

		static void DeductCost(World world, string ownerId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
				bool deducted = false;
				foreach (var arch in world.GetMatchingArchetypes(req, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId != ownerId || resources[i].ResourceId != cost.ResourceId) { continue; }
						resources[i].Value -= cost.Amount;
						deducted = true;
						break;
					}
					if (deducted) { break; }
				}
			}
		}
	}
}
