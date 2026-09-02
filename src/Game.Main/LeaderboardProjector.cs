using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	// Pull-only: the leaderboard (all orgs + all countries, sorted and placed) is only ever
	// displayed by the Leaderboard/Goals/EndGame windows and the debug control-org dropdown, so
	// building it is no longer part of VisualStateConverter.Update's per-tick pass - callers
	// project on demand (open, post-command, or a coarse refresh timer while a window stays open).
	public static class LeaderboardProjector {
		public static void Project(IReadOnlyWorld world, LeaderboardState state, ResourceQuery resources, CountryConfig? countryConfig) {
			var organizations = BuildOrganizations(world, resources);
			var countries = BuildCountries(world, resources, countryConfig);
			SortAndAssignPlaces(organizations);
			SortAndAssignPlaces(countries);
			state.Set(organizations, countries);
		}

		static List<LeaderboardEntryState> BuildOrganizations(IReadOnlyWorld world, ResourceQuery resources) {
			var organizations = new List<LeaderboardEntryState>();
			int[] orgRequired = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string orgId = orgs[i].OrganizationId;
					organizations.Add(new LeaderboardEntryState(
						0,
						orgId,
						string.IsNullOrEmpty(orgs[i].DisplayName) ? orgId : orgs[i].DisplayName,
						resources.GetValue(world, orgId, ResourceDefinitions.OrgScore)));
				}
			}
			return organizations;
		}

		static List<LeaderboardEntryState> BuildCountries(IReadOnlyWorld world, ResourceQuery resources, CountryConfig? countryConfig) {
			var countries = new List<LeaderboardEntryState>();
			foreach (string countryId in GetCountryIds(world)) {
				countries.Add(new LeaderboardEntryState(
					0,
					countryId,
					GetCountryDisplayName(countryConfig, countryId),
					resources.GetValue(world, countryId, ResourceDefinitions.CountryScore)));
			}
			return countries;
		}

		static IReadOnlyList<string> GetCountryIds(IReadOnlyWorld world) {
			var ids = new List<string>();
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					ids.Add(countries[i].CountryId);
				}
			}
			ids.Sort(StringComparer.Ordinal);
			return ids;
		}

		static string GetCountryDisplayName(CountryConfig? countryConfig, string countryId) {
			var entry = countryConfig?.FindByCountryId(countryId);
			if (entry != null && !string.IsNullOrEmpty(entry.DisplayName)) {
				return entry.DisplayName;
			}
			return countryId;
		}

		static void SortAndAssignPlaces(List<LeaderboardEntryState> entries) {
			entries.Sort((a, b) => {
				int scoreCompare = b.Score.CompareTo(a.Score);
				if (scoreCompare != 0) {
					return scoreCompare;
				}
				int nameCompare = StringComparer.Ordinal.Compare(a.DisplayName, b.DisplayName);
				if (nameCompare != 0) {
					return nameCompare;
				}
				return StringComparer.Ordinal.Compare(a.EntityId, b.EntityId);
			});

			for (int i = 0; i < entries.Count; i++) {
				var entry = entries[i];
				entries[i] = new LeaderboardEntryState(i + 1, entry.EntityId, entry.DisplayName, entry.Score);
			}
		}
	}
}
