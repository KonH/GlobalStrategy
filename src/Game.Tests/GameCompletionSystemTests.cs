using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class GameCompletionSystemTests {
		sealed class QualifyingOrganizations : ICompletionCondition {
			readonly HashSet<string> _organizationIds;

			public QualifyingOrganizations(params string[] organizationIds) {
				_organizationIds = new HashSet<string>(organizationIds);
			}

			public bool IsMet(CompletionConditionContext context) {
				return _organizationIds.Contains(context.OrganizationId);
			}
		}

		static int AddCompletion(World world) {
			int entity = world.Create();
			world.Add(entity, new GameCompletion { WinnerOrganizationId = "" });
			return entity;
		}

		static void AddCountry(World world, string countryId) {
			int entity = world.Create();
			world.Add(entity, new Country { CountryId = countryId });
		}

		static int AddParticipant(World world, string organizationId, int order) {
			int entity = world.Create();
			world.Add(entity, new Organization { OrganizationId = organizationId });
			world.Add(entity, new OrganizationGameOutcome {
				ParticipationOrder = order,
				Result = OrganizationGameResult.InProgress
			});
			return entity;
		}

		[Fact]
		void no_countries_or_no_participants_leaves_the_game_in_progress() {
			var noCountries = new World();
			int noCountriesCompletion = AddCompletion(noCountries);
			AddParticipant(noCountries, "A", 0);
			GameCompletionSystem.Update(noCountries, noCountriesCompletion, new QualifyingOrganizations("A"), 100);
			Assert.False(noCountries.Get<GameCompletion>(noCountriesCompletion).IsCompleted);

			var noParticipants = new World();
			int noParticipantsCompletion = AddCompletion(noParticipants);
			AddCountry(noParticipants, "Country");
			GameCompletionSystem.Update(noParticipants, noParticipantsCompletion, new QualifyingOrganizations("A"), 100);
			Assert.False(noParticipants.Get<GameCompletion>(noParticipantsCompletion).IsCompleted);
		}

		[Fact]
		void no_qualifier_preserves_all_in_progress_results() {
			var world = new World();
			int completion = AddCompletion(world);
			AddCountry(world, "Country");
			int first = AddParticipant(world, "A", 0);
			int second = AddParticipant(world, "B", 1);

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations(), 100);

			Assert.False(world.Get<GameCompletion>(completion).IsCompleted);
			Assert.Equal(OrganizationGameResult.InProgress, world.Get<OrganizationGameOutcome>(first).Result);
			Assert.Equal(OrganizationGameResult.InProgress, world.Get<OrganizationGameOutcome>(second).Result);
		}

		[Fact]
		void simultaneous_qualifiers_choose_stable_order_and_assign_exactly_one_winner() {
			var world = new World();
			int completion = AddCompletion(world);
			AddCountry(world, "Country");
			int later = AddParticipant(world, "A", 1);
			int first = AddParticipant(world, "B", 0);
			int other = AddParticipant(world, "C", 2);

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("A", "B"), 100);

			Assert.True(world.Get<GameCompletion>(completion).IsCompleted);
			Assert.Equal("B", world.Get<GameCompletion>(completion).WinnerOrganizationId);
			Assert.Equal(OrganizationGameResult.Winner, world.Get<OrganizationGameOutcome>(first).Result);
			Assert.Equal(OrganizationGameResult.Loser, world.Get<OrganizationGameOutcome>(later).Result);
			Assert.Equal(OrganizationGameResult.Loser, world.Get<OrganizationGameOutcome>(other).Result);
		}

		[Fact]
		void repeated_evaluation_preserves_the_first_terminal_result() {
			var world = new World();
			int completion = AddCompletion(world);
			AddCountry(world, "Country");
			int first = AddParticipant(world, "A", 0);
			int second = AddParticipant(world, "B", 1);
			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("A"), 100);

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("B"), 100);

			Assert.Equal("A", world.Get<GameCompletion>(completion).WinnerOrganizationId);
			Assert.Equal(OrganizationGameResult.Winner, world.Get<OrganizationGameOutcome>(first).Result);
			Assert.Equal(OrganizationGameResult.Loser, world.Get<OrganizationGameOutcome>(second).Result);
		}
	}
}
