using System.Collections.Generic;
using ECS;
using GS.Game.Bots;
using GS.Game.Components;
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

		static int DiscoveredCountryCount(GameLogic logic, string orgId) {
			var obs = BotObservation.Build(logic.World, logic.ActionConfig, orgId, logic.EffectConfig);
			return obs.DiscoveredCountryIds.Count;
		}

		static BotProfile DiscoverAndControlProfile(string orgId) => new BotProfile {
			OrgId = orgId,
			Features = new List<BotFeatureSetting> {
				new BotFeatureSetting { FeatureId = DiscoverAndControlFeature.Id, Enabled = true, Parameters = new Dictionary<string, double>() }
			}
		};

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
		void world_discovery_mode_attaches_bot_after_init_and_ticks_it_on_the_next_update() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 2);
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

			// Second call: OrgB's bot now exists and is ticked before logic.Update runs.
			session.Update(24f);
			Assert.Single(observed);
			Assert.Equal(MultiOrgTestSupport.OrgB, observed[0].OrgId);
			Assert.Equal(DiscoverAndControlFeature.Id, observed[0].FeatureId);
			Assert.Equal(MultiOrgTestSupport.DiscoverActionId, observed[0].ActionId);
		}

		[Fact]
		void explicit_profile_mode_attaches_bot_for_the_player_org_which_world_discovery_would_never_mark() {
			// OrgA is the initial (player) org here, so InitSystem never marks it BotControlled —
			// world-discovery mode would never attach a bot for it. Attaching via explicitProfiles
			// must still work, proving explicit attachment is independent of the BotControlled marker.
			var ctx = MultiOrgTestSupport.BuildContext(rngSeed: 3);
			var logic = new GameLogic(ctx);
			var observed = new List<string>();
			var profiles = new List<BotProfile> { DiscoverAndControlProfile(MultiOrgTestSupport.OrgA) };
			var session = BotSession.Create(logic, rngSeed: 3, explicitProfiles: profiles, onAction: (orgId, featureId, actionId, countryId) => {
				observed.Add(actionId);
			});

			session.Update(0f);
			session.Update(24f);

			Assert.Single(observed);
			Assert.Equal(MultiOrgTestSupport.DiscoverActionId, observed[0]);
		}

		[Fact]
		void update_ticks_the_bot_before_game_logic_update_so_the_play_is_visible_in_the_same_call() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 4);
			var logic = new GameLogic(ctx);
			logic.Update(0f); // init only, no BotSession involved yet

			int discoveredBefore = DiscoveredCountryCount(logic, MultiOrgTestSupport.OrgB);

			var profiles = new List<BotProfile> { DiscoverAndControlProfile(MultiOrgTestSupport.OrgB) };
			var session = BotSession.Create(logic, rngSeed: 4, explicitProfiles: profiles);

			// A single Update() call must both decide (play the discover card) and process that
			// command (CreateActionEffectSystem/DiscoverCountrySystem) in the same call —
			// DiscoverCountrySystem always adds exactly one DiscoveredCountry entity whenever
			// undiscovered candidates remain, so this is a deterministic, non-rng-sensitive signal.
			session.Update(24f);

			Assert.Equal(discoveredBefore + 1, DiscoveredCountryCount(logic, MultiOrgTestSupport.OrgB));
		}

		[Fact]
		void on_action_fires_exactly_once_per_real_play_and_not_for_gated_or_empty_ticks() {
			var ctx = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 5);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			var observed = new List<string>();
			var profiles = new List<BotProfile> { DiscoverAndControlProfile(MultiOrgTestSupport.OrgB) };
			var session = BotSession.Create(logic, rngSeed: 5, explicitProfiles: profiles, onAction: (orgId, featureId, actionId, countryId) => {
				observed.Add(actionId);
			});

			// Both calls use deltaTime 0 so the simulated day never advances between them — the
			// first tick reads "day 1" (world time unmoved since manual init above), plays, and
			// records _lastActedDate = day 1; the second tick reads the same "day 1" again (still
			// unmoved), so day-gating inside Bot must suppress the second decision.
			session.Update(0f);
			Assert.Single(observed);

			session.Update(0f);
			Assert.Single(observed);

			// Org with zero cards/context (never participates) — the feature finds nothing eligible,
			// so it must never invoke the observer even though the bot is attached and ticked.
			var emptyOrgObserved = new List<string>();
			var emptyProfiles = new List<BotProfile> { DiscoverAndControlProfile(MultiOrgTestSupport.OrgC) };
			var emptyOrgSession = BotSession.Create(logic, rngSeed: 5, explicitProfiles: emptyProfiles, onAction: (orgId, featureId, actionId, countryId) => {
				emptyOrgObserved.Add(actionId);
			});
			emptyOrgSession.Update(24f);
			Assert.Empty(emptyOrgObserved);
		}

		[Fact]
		void two_sessions_from_different_game_logic_instances_do_not_share_bot_state() {
			var ctxA = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 6);
			var logicA = new GameLogic(ctxA);
			var sessionA = BotSession.Create(logicA, rngSeed: 6);

			var ctxB = MultiOrgTestSupport.BuildContext(participatingOrganizationIds: Participants, rngSeed: 6);
			var logicB = new GameLogic(ctxB);
			var sessionB = BotSession.Create(logicB, rngSeed: 6);

			// Drive only session A far enough to discover-and-attach its bot and let it act.
			sessionA.Update(0f);
			sessionA.Update(24f);
			sessionA.Update(24f);

			// Session B is only initialized once — its bot (if any) has had no chance to attach/act
			// yet, since discovery happens after the tick phase of its own first Update call.
			sessionB.Update(0f);

			Assert.NotEmpty(ReadBotActionLogEntries(logicA.World));
			Assert.Empty(ReadBotActionLogEntries(logicB.World));
		}
	}
}
