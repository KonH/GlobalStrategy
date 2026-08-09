using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	/// <summary>
	/// Precomputed per-tick world aggregates — per-country/org control totals and the set of
	/// countries currently at war — that ActionPlayability/CountryActionConditionContext can
	/// consult instead of rescanning ControlEffect/WarParticipant entities for every
	/// (card, country) pair checked in a hot loop (e.g. BotObservation's cards x countries
	/// scan). Optional everywhere it's accepted; omitting it (null) falls back to the
	/// original live ControlQuery/Wars scan per call. See .tmp/performance.md Fix 1.
	/// </summary>
	public sealed class ControlWarSnapshot {
		public IReadOnlyDictionary<string, Dictionary<string, int>> ControlByCountry { get; }
		public HashSet<string> CountriesAtWar { get; }

		public ControlWarSnapshot(
			IReadOnlyDictionary<string, Dictionary<string, int>> controlByCountry,
			HashSet<string> countriesAtWar) {
			ControlByCountry = controlByCountry;
			CountriesAtWar = countriesAtWar;
		}

		/// <param name="countryIdFilter">
		/// When supplied, control entries for countries outside this set are skipped — matches
		/// callers (e.g. BotObservation) that only care about a known set of Country entities.
		/// </param>
		public static ControlWarSnapshot Build(IReadOnlyWorld world, ISet<string>? countryIdFilter = null) {
			var controlByCountry = new Dictionary<string, Dictionary<string, int>>();
			int[] controlReq = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(controlReq, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					string countryId = effects[i].CountryId;
					if (countryIdFilter != null && !countryIdFilter.Contains(countryId)) { continue; }
					if (!controlByCountry.TryGetValue(countryId, out var byOrg)) {
						byOrg = new Dictionary<string, int>();
						controlByCountry[countryId] = byOrg;
					}
					byOrg.TryGetValue(effects[i].OrgId, out int existing);
					byOrg[effects[i].OrgId] = existing + effects[i].Value;
				}
			}

			var countriesAtWar = new HashSet<string>(System.StringComparer.Ordinal);
			int[] warReq = { TypeId<WarParticipant>.Value };
			foreach (var arch in world.GetMatchingArchetypes(warReq, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					countriesAtWar.Add(participants[i].CountryId);
				}
			}

			return new ControlWarSnapshot(controlByCountry, countriesAtWar);
		}

		public int GetOrgControl(string orgId, string countryId) {
			return ControlByCountry.TryGetValue(countryId, out Dictionary<string, int>? byOrg)
				&& byOrg.TryGetValue(orgId, out int value) ? value : 0;
		}

		public int GetTotalControl(string countryId) {
			if (!ControlByCountry.TryGetValue(countryId, out Dictionary<string, int>? byOrg)) { return 0; }
			int total = 0;
			foreach (int control in byOrg.Values) { total += control; }
			return total;
		}

		public bool IsCountryAtWar(string countryId) => CountriesAtWar.Contains(countryId);
	}
}
