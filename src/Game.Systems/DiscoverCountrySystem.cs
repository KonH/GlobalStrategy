using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class DiscoverCountrySystem {
		public static void Update(World world, int proximityEntity, Random rng) {
			int[] required = { TypeId<DiscoverCountryEffect>.Value };
			bool hasEffect = false;
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) { hasEffect = true; break; }
			}
			if (!hasEffect) { return; }

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
			bool hasPm = proximityEntity >= 0;
			if (hasPm) { pm = world.Get<ProximityMapData>(proximityEntity); }

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

			float minChance = 0.01f;
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
				if (arch.Count > 0) { return arch.GetColumn<Country>()[0].CountryId; }
			}
			return "";
		}
	}
}
