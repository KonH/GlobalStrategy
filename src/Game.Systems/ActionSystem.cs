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
		}

		public static ActionResult ProcessPlayAction(
			World world,
			PlayActionCommand cmd,
			ActionConfig actionConfig,
			int proximityEntity,
			Random rng) {
			var result = new ActionResult();
			if (string.IsNullOrEmpty(cmd.OwnerId)) { return result; }

			var actionDef = actionConfig.Find(cmd.ActionId);
			if (actionDef == null) { return result; }

			if (!CanAfford(world, cmd.OwnerId, actionDef.Prices)) { return result; }
			DeductPrices(world, cmd.OwnerId, actionDef.Prices);

			result.Executed = true;

			float roll = (float)rng.NextDouble();
			result.Success = roll < actionDef.SuccessRate;

			ReturnCardToDeck(world, cmd.OwnerId, cmd.ActionId);
			DrawCard(world, cmd.OwnerId, rng);

			if (result.Success) {
				ApplyDiscoverCountry(world, cmd.OwnerId, actionDef, proximityEntity, rng);
			}

			return result;
		}

		static bool CanAfford(World world, string ownerId, List<ActionPrice> prices) {
			foreach (var price in prices) {
				if (!HasResource(world, ownerId, price.ResourceId, price.Amount)) { return false; }
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

		static void DeductPrices(World world, string ownerId, List<ActionPrice> prices) {
			foreach (var price in prices) {
				int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
				foreach (var arch in world.GetMatchingArchetypes(req, null)) {
					ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
					Resource[] resources = arch.GetColumn<Resource>();
					int count = arch.Count;
					for (int i = 0; i < count; i++) {
						if (owners[i].OwnerId != ownerId || resources[i].ResourceId != price.ResourceId) { continue; }
						resources[i].Value -= price.Amount;
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
			int proximityEntity,
			Random rng) {

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
			float minChance = actionDef.MinCountryChance;

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
