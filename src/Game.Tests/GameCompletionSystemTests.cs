using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class GameCompletionSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
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
		void get_available_country_ids_omits_destroyed_countries() {
			var world = new World();
			AddCountry(world, "Alive");
			int destroyed = world.Create();
			world.Add(destroyed, new Country { CountryId = "Dead" });
			world.Add(destroyed, new IsDestroyed());

			HashSet<string> available = GameCompletionSystem.GetAvailableCountryIds(world);

			Assert.Contains("Alive", available);
			Assert.DoesNotContain("Dead", available);
		}

		[Fact]
		void get_available_org_ids_omits_destroyed_organizations() {
			var world = new World();
			AddParticipant(world, "Alive", 0);
			int destroyed = AddParticipant(world, "Dead", 1);
			world.Add(destroyed, new IsOrgDestroyed());

			HashSet<string> available = GameCompletionSystem.GetAvailableOrgIds(world);

			Assert.Contains("Alive", available);
			Assert.DoesNotContain("Dead", available);
		}

		[Fact]
		void control_condition_with_no_countries_or_no_participants_leaves_the_game_in_progress() {
			var noCountries = new World();
			int noCountriesCompletion = AddCompletion(noCountries);
			AddParticipant(noCountries, "A", 0);
			GameCompletionSystem.Update(noCountries, noCountriesCompletion, new TotalControlCondition(0.8), 100, _resources);
			Assert.False(noCountries.Get<GameCompletion>(noCountriesCompletion).IsCompleted);

			var noParticipants = new World();
			int noParticipantsCompletion = AddCompletion(noParticipants);
			AddCountry(noParticipants, "Country");
			GameCompletionSystem.Update(noParticipants, noParticipantsCompletion, new QualifyingOrganizations("A"), 100, _resources);
			Assert.False(noParticipants.Get<GameCompletion>(noParticipantsCompletion).IsCompleted);
		}

		[Fact]
		void no_qualifier_preserves_all_in_progress_results() {
			var world = new World();
			int completion = AddCompletion(world);
			AddCountry(world, "Country");
			int first = AddParticipant(world, "A", 0);
			int second = AddParticipant(world, "B", 1);

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations(), 100, _resources);

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

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("A", "B"), 100, _resources);

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
			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("A"), 100, _resources);

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("B"), 100, _resources);

			Assert.Equal("A", world.Get<GameCompletion>(completion).WinnerOrganizationId);
			Assert.Equal(OrganizationGameResult.Winner, world.Get<OrganizationGameOutcome>(first).Result);
			Assert.Equal(OrganizationGameResult.Loser, world.Get<OrganizationGameOutcome>(second).Result);
		}

		[Fact]
		void destroyed_org_is_never_considered_as_a_winner() {
			var world = new World();
			int completion = AddCompletion(world);
			AddCountry(world, "Country");
			int destroyed = AddParticipant(world, "A", 0);
			AddParticipant(world, "B", 1);
			world.Add(destroyed, new IsOrgDestroyed());

			GameCompletionSystem.Update(world, completion, new QualifyingOrganizations("A"), 100, _resources);

			Assert.False(world.Get<GameCompletion>(completion).IsCompleted);
		}

		[Fact]
		void sole_surviving_org_wins_via_last_org_standing() {
			var world = new World();
			int completion = AddCompletion(world);
			int destroyed = AddParticipant(world, "A", 0);
			int survivor = AddParticipant(world, "B", 1);
			world.Add(destroyed, new IsOrgDestroyed());
			world.Get<OrganizationGameOutcome>(destroyed).Result = OrganizationGameResult.Loser;

			GameCompletionSystem.Update(world, completion, new LastOrgStandingCondition(), 100, _resources);

			Assert.True(world.Get<GameCompletion>(completion).IsCompleted);
			Assert.Equal("B", world.Get<GameCompletion>(completion).WinnerOrganizationId);
			Assert.Equal(OrganizationGameResult.Winner, world.Get<OrganizationGameOutcome>(survivor).Result);
		}

		[Fact]
		void destroyed_player_fallback_completes_without_a_winner_and_preserves_survivors() {
			var world = new World();
			int completion = AddCompletion(world);
			int player = AddParticipant(world, "Player", 0);
			int survivorA = AddParticipant(world, "A", 1);
			int survivorB = AddParticipant(world, "B", 2);
			world.Add(player, new IsOrgDestroyed());
			world.Get<OrganizationGameOutcome>(player).Result = OrganizationGameResult.Loser;

			GameCompletionSystem.ApplyPlayerDestroyedLoss(world, completion, "Player");

			Assert.True(world.Get<GameCompletion>(completion).IsCompleted);
			Assert.Equal("", world.Get<GameCompletion>(completion).WinnerOrganizationId);
			Assert.Equal(OrganizationGameResult.InProgress, world.Get<OrganizationGameOutcome>(survivorA).Result);
			Assert.Equal(OrganizationGameResult.InProgress, world.Get<OrganizationGameOutcome>(survivorB).Result);
		}
	}
}
