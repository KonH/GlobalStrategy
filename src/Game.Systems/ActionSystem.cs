using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ActionSystem {
		public struct ActionResult {
			public bool Executed;
			public bool Success;
			public List<IEffect> Effects;
		}

		public static ActionResult ProcessPlayAction(
			World world,
			PlayActionCommand cmd,
			ActionConfig actionConfig,
			EffectConfig effectConfig,
			int proximityEntity,
			Random rng) {
			var result = new ActionResult();
			if (string.IsNullOrEmpty(cmd.OwnerId)) { return result; }

			var actionDef = actionConfig.Find(cmd.ActionId);
			if (actionDef == null) { return result; }

			if (!CanAfford(world, cmd.OwnerId, actionDef.Cost)) { return result; }
			DeductPrices(world, cmd.OwnerId, actionDef.Cost);

			result.Executed = true;
			result.Effects = new List<IEffect>();
			foreach (var cost in actionDef.Cost) {
				if (cost.ResourceId == "gold") {
					result.Effects.Add(new ResourceChange { OwnerId = cmd.OwnerId, ResourceId = "gold", Diff = -cost.Amount });
					break;
				}
			}

			// Compute org influence for success rate
			double orgInfluence = 0;
			int[] infReq = { TypeId<InfluenceEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(infReq, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId == cmd.OwnerId) {
						orgInfluence += effects[i].Value;
					}
				}
			}

			float successRate = (float)ExpressionNode.Evaluate(actionDef.SuccessRateNode, new ExpressionContext { Influence = orgInfluence });

			float roll = (float)rng.NextDouble();
			result.Success = roll < successRate;

			ReturnCardToDeck(world, cmd.OwnerId, cmd.ActionId);
			DrawCard(world, cmd.OwnerId, rng);

			if (result.Success) {
				ApplyDiscoverCountry(world, cmd.OwnerId, actionDef, effectConfig, proximityEntity, rng);
			}

			return result;
		}

		static bool CanAfford(World world, string ownerId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				if (!HasResource(world, ownerId, cost.ResourceId, cost.Amount)) { return false; }
			}
			return true;
		}

		static bool HasResource(World world, string ownerId, string resourceId, double amount) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId != ownerId || resources[i].ResourceId != resourceId) { continue; }
					return resources[i].Value >= amount;
				}
			}
			return false;
		}

		static void DeductPrices(World world, string ownerId, List<ActionCost> costs) {
			foreach (var cost in costs) {
				int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId != ownerId || resources[i].ResourceId != cost.ResourceId) { continue; }
						resources[i].Value -= cost.Amount;
						break;
					}
				}
			}
		}

		static void ReturnCardToDeck(World world, string ownerId, string actionId) {
			int[] req = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId != ownerId || cards[i].ActionId != actionId) { continue; }
					world.Remove<InHand>(arch.Entities[i]);
					return;
				}
			}
		}

		static void DrawCard(World world, string ownerId, Random rng) {
			int handSize = 1;
			int[] ownerReq = { TypeId<ActionOwner>.Value };
			foreach (var arch in world.GetMatchingArchetypes(ownerReq, null)) {
				ActionOwner[] owners = arch.GetColumn<ActionOwner>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == ownerId) { handSize = owners[i].HandSize; break; }
				}
			}

			int currentHand = 0;
			int[] handReq = { TypeId<ActionCard>.Value, TypeId<InHand>.Value };
			foreach (var arch in world.GetMatchingArchetypes(handReq, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId == ownerId) { currentHand++; }
				}
			}

			int toDraw = handSize - currentHand;
			if (toDraw <= 0) { return; }

			int[] deckReq = { TypeId<ActionCard>.Value };
			int[] excludeHand = { TypeId<InHand>.Value };
			var deckEntities = new List<int>();
			foreach (var arch in world.GetMatchingArchetypes(deckReq, excludeHand)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId == ownerId) { deckEntities.Add(arch.Entities[i]); }
				}
			}

			for (int i = deckEntities.Count - 1; i > 0; i--) {
				int j = rng.Next(i + 1);
				var tmp = deckEntities[i]; deckEntities[i] = deckEntities[j]; deckEntities[j] = tmp;
			}

			int slot = currentHand;
			for (int k = 0; k < toDraw && k < deckEntities.Count; k++) {
				world.Add(deckEntities[k], new InHand { SlotIndex = slot++ });
			}
		}

		static void ApplyDiscoverCountry(
			World world,
			string ownerId,
			ActionDefinition actionDef,
			EffectConfig effectConfig,
			int proximityEntity,
			Random rng) {

			// Find MinCountryChance from DiscoverCountryEffectParams
			float minChance = 0.01f;
			foreach (var effectId in actionDef.EffectIds) {
				var effectEntry = effectConfig.Find(effectId);
				if (effectEntry is DiscoverCountryEffectParams discoverParams) {
					minChance = (float)discoverParams.MinCountryChance;
					break;
				}
			}

			string playerCountryId = FindPlayerCountryId(world);

			var discoveredSet = new HashSet<string>();
			int[] discReq = { TypeId<Country>.Value, TypeId<IsDiscovered>.Value };
			foreach (var arch in world.GetMatchingArchetypes(discReq, null)) {
				Country[] cs = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) { discoveredSet.Add(cs[i].CountryId); }
			}

			var allCountries = new List<(int entity, string countryId)>();
			int[] countryReq = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
				Country[] cs = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) {
					if (!discoveredSet.Contains(cs[i].CountryId)) {
						allCountries.Add((arch.Entities[i], cs[i].CountryId));
					}
				}
			}
			if (allCountries.Count == 0) { return; }

			ProximityMapData pm = default;
			bool hasPm = false;
			if (proximityEntity >= 0) {
				pm = world.Get<ProximityMapData>(proximityEntity);
				hasPm = true;
			}

			float totalWeight = 0f;
			var weights = new float[allCountries.Count];

			for (int i = 0; i < allCountries.Count; i++) {
				string b = allCountries[i].countryId;
				float w = 1f;
				if (hasPm && pm.Distances != null && !string.IsNullOrEmpty(playerCountryId)) {
					string a = playerCountryId;
					string ka = string.CompareOrdinal(a, b) <= 0 ? a : b;
					string kb = string.CompareOrdinal(a, b) <= 0 ? b : a;
					if (pm.Distances.TryGetValue((ka, kb), out float d)) {
						w = d < 0.0001f ? 1e6f : 1f / d;
					}
				}
				weights[i] = w;
				totalWeight += w;
			}

			// Lift any weight below the floor in a single pass using the pre-floor total.
			// A converging loop would oscillate when minChance > 1/N (floor > mean weight).
			float floorWeight = minChance * totalWeight;
			for (int i = 0; i < weights.Length; i++) {
				if (weights[i] < floorWeight) { weights[i] = floorWeight; }
			}

			float wTotal = 0f;
			for (int i = 0; i < weights.Length; i++) { wTotal += weights[i]; }
			float pick = (float)rng.NextDouble() * wTotal;
			float acc = 0f;
			int chosen = 0;
			for (int i = 0; i < weights.Length; i++) {
				acc += weights[i];
				if (pick <= acc) { chosen = i; break; }
			}

			world.Add(allCountries[chosen].entity, new IsDiscovered());
		}

		public static ActionResult ProcessPlayCountryAction(
			World world,
			PlayCountryActionCommand cmd,
			ActionConfig config,
			EffectConfig effectConfig,
			DateTime currentTime,
			Random rng) {
			var result = new ActionResult();
			result.Effects = new List<IEffect>();

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
			if (!CanAfford(world, cmd.OrgId, def.Cost)) { return result; }
			DeductPrices(world, cmd.OrgId, def.Cost);

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
							result.Effects.Add(new InfluenceAdded { OrgId = cmd.OrgId, CountryId = cmd.CountryId, Amount = toAdd });
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
									result.Effects.Add(new CharacterOpinionChange { CountryId = cmd.CountryId, CharacterId = cmd.TargetCharacterId, Diff = opinionParams.InitialValue });
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

		static string FindPlayerCountryId(World world) {
			int[] req = { TypeId<Country>.Value, TypeId<Player>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<Country>()[0].CountryId;
				}
			}
			return "";
		}
	}
}
