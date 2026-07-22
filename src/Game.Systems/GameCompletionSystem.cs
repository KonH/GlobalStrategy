using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class GameCompletionSystem {
		public static void Update(
			World world,
			int completionEntity,
			ICompletionCondition condition,
			int maxControlPool) {
			if (world == null) {
				throw new ArgumentNullException(nameof(world));
			}
			if (condition == null) {
				throw new ArgumentNullException(nameof(condition));
			}

			ref GameCompletion completion = ref world.Get<GameCompletion>(completionEntity);
			if (completion.IsCompleted) {
				return;
			}

			var countryIds = GetAvailableCountryIds(world);
			if (countryIds.Count == 0) {
				return;
			}

			var participants = GetParticipants(world);
			if (participants.Count == 0) {
				return;
			}

			participants.Sort(CompareParticipants);
			Participant? winner = null;
			foreach (Participant participant in participants) {
				var context = new CompletionConditionContext(
					world,
					participant.OrganizationId,
					countryIds,
					maxControlPool);
				if (condition.IsMet(context)) {
					winner = participant;
					break;
				}
			}

			if (!winner.HasValue) {
				return;
			}

			string winnerOrganizationId = winner.Value.OrganizationId;
			foreach (Participant participant in participants) {
				ref OrganizationGameOutcome outcome = ref world.Get<OrganizationGameOutcome>(participant.Entity);
				outcome.Result = participant.Entity == winner.Value.Entity
					? OrganizationGameResult.Winner
					: OrganizationGameResult.Loser;
			}

			completion.WinnerOrganizationId = winnerOrganizationId;
			completion.IsCompleted = true;
		}

		static HashSet<string> GetAvailableCountryIds(IReadOnlyWorld world) {
			var countryIds = new HashSet<string>(StringComparer.Ordinal);
			int[] required = { TypeId<Country>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Country[] countries = archetype.GetColumn<Country>();
				for (int i = 0; i < archetype.Count; i++) {
					countryIds.Add(countries[i].CountryId);
				}
			}
			return countryIds;
		}

		static List<Participant> GetParticipants(IReadOnlyWorld world) {
			var participants = new List<Participant>();
			int[] required = {
				TypeId<Organization>.Value,
				TypeId<OrganizationGameOutcome>.Value
			};
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				OrganizationGameOutcome[] outcomes = archetype.GetColumn<OrganizationGameOutcome>();
				for (int i = 0; i < archetype.Count; i++) {
					participants.Add(new Participant(
						archetype.Entities[i],
						organizations[i].OrganizationId,
						outcomes[i].ParticipationOrder));
				}
			}
			return participants;
		}

		static int CompareParticipants(Participant left, Participant right) {
			int orderComparison = left.ParticipationOrder.CompareTo(right.ParticipationOrder);
			return orderComparison != 0
				? orderComparison
				: string.CompareOrdinal(left.OrganizationId, right.OrganizationId);
		}

		readonly struct Participant {
			public int Entity { get; }
			public string OrganizationId { get; }
			public int ParticipationOrder { get; }

			public Participant(int entity, string organizationId, int participationOrder) {
				Entity = entity;
				OrganizationId = organizationId;
				ParticipationOrder = participationOrder;
			}
		}
	}
}
