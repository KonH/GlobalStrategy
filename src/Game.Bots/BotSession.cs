using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Main;

namespace GS.Game.Bots {
	public delegate void BotActionObserver(string orgId, string featureId, string actionId, string countryId);

	public sealed class BotSession {
		readonly GameLogic _logic;
		readonly int? _rngSeed;
		readonly BotFeatureRegistry _registry;
		readonly IGameLogger? _logger;
		readonly BotActionObserver? _onAction;
		readonly Dictionary<string, Bot> _botsByOrgId = new();

		public GameLogic Logic => _logic;

		readonly bool _discoverFromWorld;

		public static BotSession Create(
				GameLogic logic,
				int? rngSeed,
				IReadOnlyList<BotProfile>? explicitProfiles = null,
				BotFeatureRegistry? registry = null,
				IGameLogger? logger = null,
				BotActionObserver? onAction = null,
				bool discoverFromWorld = true) {
			var session = new BotSession(logic, rngSeed, registry ?? BotFeatureRegistry.CreateDefault(logic.MaxControlPool), logger, onAction, discoverFromWorld);
			if (explicitProfiles != null) {
				foreach (var profile in explicitProfiles) {
					session.AttachBot(profile.OrgId, profile);
				}
			}
			return session;
		}

		BotSession(GameLogic logic, int? rngSeed, BotFeatureRegistry registry, IGameLogger? logger, BotActionObserver? onAction, bool discoverFromWorld) {
			_logic = logic;
			_rngSeed = rngSeed;
			_registry = registry;
			_logger = logger;
			_onAction = onAction;
			_discoverFromWorld = discoverFromWorld;
		}

		public void Update(float deltaTime) {
			if (!_logic.IsCompleted) {
				foreach (var bot in _botsByOrgId.Values) {
					bot.ExecuteDecisionTick(_logic.World, _logic.ActionConfig);
				}
			}
			_logic.Update(deltaTime);
			if (_discoverFromWorld) {
				SyncBotsFromWorld();
			}
		}

		void SyncBotsFromWorld() {
			int[] required = { TypeId<Organization>.Value, TypeId<BotControlled>.Value };
			foreach (var arch in _logic.World.GetMatchingArchetypes(required, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				for (int i = 0; i < arch.Count; i++) {
					string orgId = orgs[i].OrganizationId;
					if (!_botsByOrgId.ContainsKey(orgId)) {
						AttachBot(orgId, DefaultProfile(orgId));
					}
				}
			}
		}

		void AttachBot(string orgId, BotProfile profile) {
			var features = new List<IBotFeature>();
			foreach (var featureSetting in profile.Features) {
				if (featureSetting.Enabled) {
					features.Add(_registry.Create(featureSetting.FeatureId, featureSetting.Parameters));
				}
			}
			var rng = BotRng.Create(_rngSeed ?? 0, orgId);
			Bot bot = null!;
			BotEmissionCallback callback = (actionId, countryId) => {
				string featureId = bot.CurrentFeatureId;
				_logic.RecordBotAction(orgId, featureId, actionId, countryId);
				_onAction?.Invoke(orgId, featureId, actionId, countryId);
			};
			var sink = new BotCommandSink(orgId, _logic.Commands, _logger, callback);
			bot = new Bot(orgId, features, rng, sink, _logic.EffectConfig);
			_botsByOrgId[orgId] = bot;
		}

		// Feature set for auto-attached (world-discovered) bots comes from
		// GameSettings.BotFeatures (Assets/Configs/game_settings.json), not a
		// hardcoded literal here - the eval-validated discoverAndControl threshold lives
		// in config so it can be tuned without a code change.
		BotProfile DefaultProfile(string orgId) {
			var features = new List<BotFeatureSetting>();
			foreach (var entry in _logic.BotFeatures) {
				features.Add(new BotFeatureSetting { FeatureId = entry.FeatureId, Enabled = entry.Enabled, Parameters = entry.Parameters });
			}
			return new BotProfile { OrgId = orgId, Features = features };
		}
	}
}
