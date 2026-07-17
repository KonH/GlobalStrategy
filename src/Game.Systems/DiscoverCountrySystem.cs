using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class DiscoverCountrySystem {
		public static void Update(World world, int proximityEntity, Random rng,
			string viewOrgId, IReadOnlyDictionary<string, string> hqCountryByOrgId) {

			int[] required = { TypeId<DiscoverCountryEffect>.Value };
			var orgIds = new List<string>();
			var seenOrgIds = new HashSet<string>();
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				DiscoverCountryEffect[] effects = arch.GetColumn<DiscoverCountryEffect>();
				for (int i = 0; i < arch.Count; i++) {
					string orgId = effects[i].OrgId;
					if (seenOrgIds.Add(orgId)) { orgIds.Add(orgId); }
				}
			}
			if (orgIds.Count == 0) { return; }

			string playerCountryId = FindPlayerCountryId(world);

			ProximityMapData pm = default;
			bool hasPm = proximityEntity >= 0;
			if (hasPm) { pm = world.Get<ProximityMapData>(proximityEntity); }

			foreach (string orgId in orgIds) {
				ResolveDiscoveryForOrg(world, rng, orgId, viewOrgId, playerCountryId, hqCountryByOrgId, hasPm, pm);
			}
		}

		static void ResolveDiscoveryForOrg(
			World world, Random rng, string orgId, string viewOrgId, string playerCountryId,
			IReadOnlyDictionary<string, string> hqCountryByOrgId, bool hasPm, ProximityMapData pm) {

			var discoveredSet = new HashSet<string>();
			int[] discReq = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in world.GetMatchingArchetypes(discReq, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == orgId) { discoveredSet.Add(dcs[i].CountryId); }
				}
			}

			var candidates = new List<string>();
			int[] countryReq = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
				Country[] cs = arch.GetColumn<Country>();
				for (int i = 0; i < arch.Count; i++) {
					if (!discoveredSet.Contains(cs[i].CountryId)) {
						candidates.Add(cs[i].CountryId);
					}
				}
			}
			if (candidates.Count == 0) { return; }

			string anchorCountryId = "";
			if (orgId == viewOrgId && !string.IsNullOrEmpty(playerCountryId)) {
				anchorCountryId = playerCountryId;
			} else if (hqCountryByOrgId.TryGetValue(orgId, out var hq) && !string.IsNullOrEmpty(hq)) {
				anchorCountryId = hq;
			}

			float totalWeight = 0f;
			var weights = new float[candidates.Count];
			for (int i = 0; i < candidates.Count; i++) {
				string b = candidates[i];
				float w = 1f;
				if (hasPm && pm.Distances != null && !string.IsNullOrEmpty(anchorCountryId)) {
					string a = anchorCountryId;
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

			int newEntity = world.Create();
			world.Add(newEntity, new DiscoveredCountry { OrgId = orgId, CountryId = candidates[chosen] });
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
