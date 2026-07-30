using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;

namespace GS.Main {
	public static class WarIconsProjector {
		sealed class ParticipantGroup {
			public SortedSet<string> Attackers { get; } = new SortedSet<string>(StringComparer.Ordinal);
			public SortedSet<string> Defenders { get; } = new SortedSet<string>(StringComparer.Ordinal);

			public bool IsRelevant(HashSet<string> relevantCountryIds) {
				return Attackers.Overlaps(relevantCountryIds) || Defenders.Overlaps(relevantCountryIds);
			}
		}

		public static List<WarIconEntryState> Build(IReadOnlyWorld world, string playerOrgId) {
			var result = new List<WarIconEntryState>();
			if (string.IsNullOrEmpty(playerOrgId)) {
				return result;
			}

			HashSet<string> relevantCountryIds = BuildRelevantCountryIds(world, playerOrgId);
			if (relevantCountryIds.Count == 0) {
				return result;
			}

			Dictionary<string, ParticipantGroup> participantsByWarId = BuildParticipantGroups(world);
			var addedWarIds = new HashSet<string>(StringComparer.Ordinal);
			int[] warRequired = { TypeId<War>.Value, TypeId<WarProgress>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(warRequired, null)) {
				War[] wars = archetype.GetColumn<War>();
				WarProgress[] progresses = archetype.GetColumn<WarProgress>();
				int count = archetype.Count;
				for (int i = 0; i < count; i++) {
					string warId = wars[i].WarId;
					if (string.IsNullOrEmpty(warId)
						|| addedWarIds.Contains(warId)
						|| !participantsByWarId.TryGetValue(warId, out ParticipantGroup? participants)
						|| participants.Attackers.Count == 0
						|| participants.Defenders.Count == 0
						|| !participants.IsRelevant(relevantCountryIds)) {
						continue;
					}

					result.Add(new WarIconEntryState(
						warId,
						progresses[i].Value,
						GetFirst(participants.Attackers),
						GetFirst(participants.Defenders)));
					addedWarIds.Add(warId);
				}
			}

			result.Sort((a, b) => StringComparer.Ordinal.Compare(a.WarId, b.WarId));
			return result;
		}

		static HashSet<string> BuildRelevantCountryIds(IReadOnlyWorld world, string playerOrgId) {
			var controlByCountryId = new Dictionary<string, int>(StringComparer.Ordinal);
			int[] controlRequired = { TypeId<ControlEffect>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(controlRequired, null)) {
				ControlEffect[] effects = archetype.GetColumn<ControlEffect>();
				int count = archetype.Count;
				for (int i = 0; i < count; i++) {
					ControlEffect effect = effects[i];
					if (effect.OrgId != playerOrgId || string.IsNullOrEmpty(effect.CountryId)) {
						continue;
					}
					controlByCountryId.TryGetValue(effect.CountryId, out int current);
					controlByCountryId[effect.CountryId] = current + effect.Value;
				}
			}

			var relevantCountryIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (var entry in controlByCountryId) {
				if (entry.Value > 0) {
					relevantCountryIds.Add(entry.Key);
				}
			}
			return relevantCountryIds;
		}

		static Dictionary<string, ParticipantGroup> BuildParticipantGroups(IReadOnlyWorld world) {
			var participantsByWarId = new Dictionary<string, ParticipantGroup>(StringComparer.Ordinal);
			int[] participantRequired = { TypeId<WarParticipant>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(participantRequired, null)) {
				WarParticipant[] participants = archetype.GetColumn<WarParticipant>();
				int count = archetype.Count;
				for (int i = 0; i < count; i++) {
					WarParticipant participant = participants[i];
					if (string.IsNullOrEmpty(participant.WarId) || string.IsNullOrEmpty(participant.CountryId)) {
						continue;
					}
					if (!participantsByWarId.TryGetValue(participant.WarId, out ParticipantGroup? group)) {
						group = new ParticipantGroup();
						participantsByWarId.Add(participant.WarId, group);
					}
					if (participant.Kind == WarParticipantKind.Attacker) {
						group.Attackers.Add(participant.CountryId);
					} else if (participant.Kind == WarParticipantKind.Defender) {
						group.Defenders.Add(participant.CountryId);
					}
				}
			}
			return participantsByWarId;
		}

		static string GetFirst(SortedSet<string> countryIds) {
			foreach (string countryId in countryIds) {
				return countryId;
			}
			return "";
		}
	}
}
