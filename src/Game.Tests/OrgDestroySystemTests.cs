using System.Collections.Generic;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class OrgDestroySystemTests {
		const string OrgId = "org-a";
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		[Fact]
		void positive_control_is_a_hard_destroy_gate() {
			TestWorld world = BuildWorld();
			AddControl(world, 1);

			bool destroyed = TryDestroy(world, new ActionConfig(), new EffectConfig());

			Assert.False(destroyed);
			Assert.False(world.Has<IsOrgDestroyed>(world.OrgEntity));
		}

		[Fact]
		void affordable_discard_and_non_full_hand_each_prevent_destroy() {
			TestWorld affordableWorld = BuildWorld();
			AddGold(affordableWorld, 50);
			Assert.False(TryDestroy(affordableWorld, new ActionConfig(), new EffectConfig()));

			TestWorld nonFullWorld = BuildWorld();
			AddDeck(nonFullWorld, CardOwnerKind.Country, handSize: 1);
			Assert.False(TryDestroy(nonFullWorld, new ActionConfig(), new EffectConfig()));
		}

		[Fact]
		void a_control_raising_card_in_either_pool_prevents_destroy() {
			foreach (CardOwnerKind ownerKind in new[] { CardOwnerKind.Country, CardOwnerKind.Org }) {
				TestWorld world = BuildWorld();
				AddDeck(world, ownerKind, handSize: 1);
				AddCard(world, ownerKind, "raise-control");
				ActionConfig actions = TestActionConfig.Create()
					.Action("raise-control", ownerType: "", effectIds: new[] { "raise" })
					.Build();
				var effects = new EffectConfig {
					Effects = new List<ActionEffectDefinition> {
						new ControlChangeEffectParams { EffectId = "raise", Amount = 1 }
					}
				};

				Assert.False(TryDestroy(world, actions, effects));
			}
		}

		[Fact]
		void all_conditions_add_one_flag_and_event_flip_outcome_and_purge_residual_control() {
			TestWorld world = BuildWorld();
			AddDeck(world, CardOwnerKind.Country, handSize: 1);
			AddCard(world, CardOwnerKind.Country, "too-expensive");
			AddControl(world, -1);
			ActionConfig actions = TestActionConfig.Create()
				.Action("too-expensive", ownerType: "", cost: new[] {
					new ActionCost { ResourceId = ResourceDefinitions.Gold, Amount = 100 }
				})
				.Build();

			Assert.True(TryDestroy(world, actions, new EffectConfig()));
			Assert.True(world.Has<IsOrgDestroyed>(world.OrgEntity));
			Assert.Equal(OrganizationGameResult.Loser, world.Get<OrganizationGameOutcome>(world.OrgEntity).Result);
			Assert.Equal(0, world.Count<ControlEffect>());
			Assert.Equal(1, world.Count<OrgDestroyedApplied>());

			Assert.False(TryDestroy(world, actions, new EffectConfig()));
			Assert.Equal(1, world.Count<OrgDestroyedApplied>());
		}

		[Fact]
		void cooldown_as_the_only_failure_counts_as_playable_and_prevents_destroy() {
			TestWorld world = BuildWorld();
			AddDeck(world, CardOwnerKind.Org, handSize: 1);
			AddCard(world, CardOwnerKind.Org, "cooldown-card");
			world.Entity().With(new ActionCooldownState {
				OrgId = OrgId,
				ActionId = "cooldown-card",
				EndTime = new System.DateTime(2100, 1, 1)
			});
			ActionConfig actions = TestActionConfig.Create()
				.Action("cooldown-card", ownerType: "")
				.Build();

			Assert.False(TryDestroy(world, actions, new EffectConfig()));
		}

		[Fact]
		void country_card_playable_in_any_available_country_prevents_destroy() {
			TestWorld world = BuildWorld();
			world.Country("country-b");
			AddDeck(world, CardOwnerKind.Country, handSize: 1);
			AddCard(world, CardOwnerKind.Country, "needs-country-control");
			AddControl(world, "other-org", "country-b", 1);

			Assert.False(TryDestroy(world, CountryControlActionConfig(), new EffectConfig()));
		}

		[Fact]
		void destroyed_countries_are_not_country_card_playability_contexts() {
			TestWorld world = BuildWorld();
			world.Country("country-b", destroyed: true);
			AddDeck(world, CardOwnerKind.Country, handSize: 1);
			AddCard(world, CardOwnerKind.Country, "needs-country-control");
			AddControl(world, "other-org", "country-b", 1);

			Assert.True(TryDestroy(world, CountryControlActionConfig(), new EffectConfig()));
		}

		[Fact]
		void action_effect_classifier_detects_only_positive_control_changes() {
			var effects = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new ControlChangeEffectParams { EffectId = "positive", Amount = 1 },
					new ControlChangeEffectParams { EffectId = "negative", Amount = -1 }
				}
			};

			Assert.True(ActionEffectClassifier.RaisesControl(
				new ActionDefinition { EffectIds = new List<string> { "positive" } }, effects));
			Assert.False(ActionEffectClassifier.RaisesControl(
				new ActionDefinition { EffectIds = new List<string> { "negative" } }, effects));
		}

		[Fact]
		void converter_projects_destroy_fifo_and_player_flag_before_completion() {
			TestWorld world = BuildWorld();
			world.Add(world.OrgEntity, new IsOrgDestroyed())
				.Entity().With(new OrgDestroyedApplied { OrganizationId = OrgId })
				.Entity().With(new GameCompletion { IsCompleted = true, WinnerOrganizationId = "other" })
				.GameTime()
				.Locale();
			var probe = new VisualStateProbe(resources: _resources, relations: _relations);
			var order = new List<string>();
			probe.State.OrgDestroyedResults.PropertyChanged += (_, __) => order.Add("destroy");
			probe.State.GameCompletion.PropertyChanged += (_, __) => order.Add("completion");

			probe.Update(world);

			OrgDestroyedSnapshotState snapshot = Assert.Single(probe.State.OrgDestroyedResults.Entries);
			Assert.Equal(OrgId, snapshot.OrganizationId);
			Assert.True(probe.State.PlayerOrganization.IsDestroyed);
			Assert.Equal(new[] { "destroy", "completion" }, order);
			CleanupEffectNotificationsSystem.UpdateOrgDestroyed(world);
			Assert.Equal(0, world.Count<OrgDestroyedApplied>());
		}

		bool TryDestroy(TestWorld world, ActionConfig actions, EffectConfig effects) {
			return OrgDestroySystem.TryDestroyIfConditionsMet(
				world, world.OrgEntity, actions, effects, _resources, _relations,
				new GameSettings { DiscardGoldCost = 50 }, 100);
		}

		/// <summary>The org under test (as <see cref="TestWorld.OrgEntity"/>) plus one country.</summary>
		static TestWorld BuildWorld() {
			return TestWorld.Create()
				.Org(OrgId, displayName: "")
				.With(new OrganizationGameOutcome {
					ParticipationOrder = 0,
					Result = OrganizationGameResult.InProgress
				})
				.Country("country-a");
		}

		static void AddDeck(TestWorld world, CardOwnerKind ownerKind, int handSize) {
			world.Deck(OrgId, ownerKind, handSize);
		}

		static void AddCard(TestWorld world, CardOwnerKind ownerKind, string actionId) {
			world.Card(actionId, orgId: OrgId, countryId: null, kind: ownerKind);
		}

		static void AddGold(TestWorld world, double value) {
			world.Resource(OrgId, ResourceDefinitions.Gold, value);
		}

		static void AddControl(TestWorld world, int value) {
			AddControl(world, OrgId, "country-a", value);
		}

		static void AddControl(TestWorld world, string orgId, string countryId, int value) {
			world.Control(countryId, value, orgId, $"test-control-{orgId}-{countryId}");
		}

		static ActionConfig CountryControlActionConfig() {
			return TestActionConfig.Create()
				.Action("needs-country-control", ownerType: "",
					conditions: new[] { Expr.Gte("totalCountryControl", 1) })
				.Build();
		}
	}
}
