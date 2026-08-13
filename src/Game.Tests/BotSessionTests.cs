using System.Collections.Generic;
using ECS;
using GS.Game.Bots;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class BotSessionTests {
		static readonly List<string> Participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };

		static string[] ReadBotActionLogEntries(World world) {
			int[] req = { TypeId<BotActionLog>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<BotActionLog>()[0].Entries;
				}
			}
			return System.Array.Empty<string>();
		}

		static BotProfile ControlProfile(string orgId) => new BotProfile {
			OrgId = orgId,
			Features = new List<BotFeatureSetting> {
				new BotFeatureSetting { FeatureId = ControlFeature.Id, Enabled = true, Parameters = new Dictionary<string, double>() }
			}
		};

		static int FindOrganization(World world, string orgId) {
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				Organization[] organizations = archetype.GetColumn<Organization>();
				for (int i = 0; i < archetype.Count; i++) {
					if (organizations[i].OrganizationId == orgId) {
						return archetype.Entities[i];
					}
				}
			}
			throw new System.InvalidOperationException($"Organization '{orgId}' was not found.");
		}

		[Fact]
		void no_explicit_profiles_and_no_bot_controlled_orgs_behaves_like_plain_game_logic_update() {
			// Single-org context: only the initial (player) org participates, so InitSystem never
			// marks anyone BotControlled and no explicitProfiles are supplied either.
			var ctx = MultiOrgTestSupport.BuildContext(rngSeed: 1);
			var logic = new GameLogic(ctx);
			var session = BotSession.Create(logic, rngSeed: 1);

			session.Update(0f);
			session.Update(24f);
			session.Update(24f);

			Assert.Empty(ReadBotActionLogEntries(logic.World));
		}

		[Fact]
		void world_discovery_mode_attaches_bot_after_init_then_acquires_before_playing() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 2, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			var observed = new List<(string OrgId, string FeatureId, string ActionId, string CountryId)>();
			var session = BotSession.Create(logic, rngSeed: 2, onAction: (orgId, featureId, actionId, countryId) => {
				observed.Add((orgId, featureId, actionId, countryId));
			});

			// First call: no bots exist yet, so this call only runs InitSystem (via logic.Update),
			// which marks OrgB BotControlled; SyncBotsFromWorld attaches it *after* the tick phase,
			// so nothing fires yet this call.
			session.Update(0f);
			Assert.Empty(observed);

			// The next two calls use the production draw/receive commands. Acquisition is not
			// reported as a played action.
			session.Update(24f);
			Assert.Empty(observed);
			session.Update(24f);
			Assert.Empty(observed);

			// Once the received card is in hand, the existing control feature can play it.
			session.Update(24f);
			Assert.Single(observed);
			Assert.Equal(MultiOrgTestSupport.OrgB, observed[0].OrgId);
			Assert.Equal(ControlFeature.Id, observed[0].FeatureId);
			Assert.Equal(MultiOrgTestSupport.CountryCardActionId, observed[0].ActionId);
		}

		[Fact]
		void explicit_profile_mode_attaches_bot_for_the_player_org_which_world_discovery_would_never_mark() {
			// OrgA is the initial (player) org here, so InitSystem never marks it BotControlled —
			// world-discovery mode would never attach a bot for it. Attaching via explicitProfiles
			// must still work, proving explicit attachment is independent of the BotControlled marker.
			var ctx = MultiOrgTestSupport.BuildContext(rngSeed: 3, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			var observed = new List<string>();
			var profiles = new List<BotProfile> { ControlProfile(MultiOrgTestSupport.OrgA) };
			var session = BotSession.Create(logic, rngSeed: 3, explicitProfiles: profiles, onAction: (orgId, featureId, actionId, countryId) => {
				observed.Add(actionId);
			});

			session.Update(0f);
			session.Update(24f);
			session.Update(24f);
			session.Update(24f);

			Assert.Single(observed);
			Assert.Equal(MultiOrgTestSupport.CountryCardActionId, observed[0]);
		}

		[Fact]
		void update_processes_each_acquisition_command_then_the_play_in_its_emitting_call() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 4, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f); // init only, no BotSession involved yet

			int controlBefore = OrgMetrics.GetTotalControl(logic.World, MultiOrgTestSupport.OrgB);

			var profiles = new List<BotProfile> { ControlProfile(MultiOrgTestSupport.OrgB) };
			var session = BotSession.Create(logic, rngSeed: 4, explicitProfiles: profiles);

			// The first two calls process draw then receive. Neither acquisition command changes control.
			session.Update(24f);
			Assert.Equal(controlBefore, OrgMetrics.GetTotalControl(logic.World, MultiOrgTestSupport.OrgB));
			session.Update(24f);
			Assert.Equal(controlBefore, OrgMetrics.GetTotalControl(logic.World, MultiOrgTestSupport.OrgB));

			// The third call emits and processes the play in that same call.
			session.Update(24f);

			Assert.True(OrgMetrics.GetTotalControl(logic.World, MultiOrgTestSupport.OrgB) > controlBefore);
		}

		[Fact]
		void on_action_fires_exactly_once_per_real_play_and_not_for_gated_or_empty_ticks() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 5, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var observed = new List<string>();
			var profiles = new List<BotProfile> { ControlProfile(MultiOrgTestSupport.OrgB) };
			var session = BotSession.Create(logic, rngSeed: 5, explicitProfiles: profiles, onAction: (orgId, featureId, actionId, countryId) => {
				observed.Add(actionId);
			});

			// Acquisition runs before the daily gate, so zero-delta updates can draw and receive
			// successively without producing action callbacks.
			session.Update(0f);
			Assert.Empty(observed);

			session.Update(0f);
			Assert.Empty(observed);

			// The received card can then be played once. A fourth same-day tick is gated.
			session.Update(0f);
			Assert.Single(observed);

			session.Update(0f);
			Assert.Single(observed);

			// Org with zero cards/context (never participates) — the feature finds nothing eligible,
			// so it must never invoke the observer even though the bot is attached and ticked.
			var emptyOrgObserved = new List<string>();
			var emptyProfiles = new List<BotProfile> { ControlProfile(MultiOrgTestSupport.OrgC) };
			var emptyOrgSession = BotSession.Create(logic, rngSeed: 5, explicitProfiles: emptyProfiles, onAction: (orgId, featureId, actionId, countryId) => {
				emptyOrgObserved.Add(actionId);
			});
			emptyOrgSession.Update(24f);
			Assert.Empty(emptyOrgObserved);
		}

		[Fact]
		void two_sessions_from_different_game_logic_instances_do_not_share_bot_state() {
			var ctxA = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 6, includeCountryCard: true);
			var logicA = new GameLogic(ctxA);
			var sessionA = BotSession.Create(logicA, rngSeed: 6);

			var ctxB = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 6, includeCountryCard: true);
			var logicB = new GameLogic(ctxB);
			var sessionB = BotSession.Create(logicB, rngSeed: 6);

			// Drive only session A far enough to discover-and-attach its bot and let it act.
			sessionA.Update(0f);
			sessionA.Update(24f);
			sessionA.Update(24f);
			sessionA.Update(24f);

			// Session B is only initialized once — its bot (if any) has had no chance to attach/act
			// yet, since world-sync attaches after the tick phase of its own first Update call.
			sessionB.Update(0f);

			Assert.NotEmpty(ReadBotActionLogEntries(logicA.World));
			Assert.Empty(ReadBotActionLogEntries(logicB.World));
		}

		[Fact]
		void terminal_tick_emits_no_bot_command_callback_or_action_log_entry() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 7, includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var observed = new List<string>();
			var profiles = new List<BotProfile> { ControlProfile(MultiOrgTestSupport.OrgB) };
			var session = BotSession.Create(logic, rngSeed: 7, explicitProfiles: profiles, onAction: (orgId, featureId, actionId, countryId) => {
				observed.Add(actionId);
			});
			int[] required = { TypeId<GameCompletion>.Value };
			foreach (var archetype in logic.World.GetMatchingArchetypes(required, null)) {
				ref GameCompletion completion = ref archetype.GetColumn<GameCompletion>()[0];
				completion.IsCompleted = true;
				completion.WinnerOrganizationId = MultiOrgTestSupport.OrgA;
				break;
			}

			session.Update(24f);

			Assert.Empty(observed);
			Assert.Empty(ReadBotActionLogEntries(logic.World));
		}

		[Fact]
		void destroyed_org_bot_is_skipped_before_its_decision_tick() {
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: Participants,
				rngSeed: 8,
				includeCountryCard: true);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var observed = new List<string>();
			var profiles = new List<BotProfile> { ControlProfile(MultiOrgTestSupport.OrgB) };
			var session = BotSession.Create(
				logic,
				rngSeed: 8,
				explicitProfiles: profiles,
				onAction: (_, _, actionId, _) => observed.Add(actionId));
			logic.World.Add(FindOrganization(logic.World, MultiOrgTestSupport.OrgB), new IsOrgDestroyed());

			session.Update(24f);
			session.Update(24f);
			session.Update(24f);

			Assert.Empty(observed);
		}
	}
}
