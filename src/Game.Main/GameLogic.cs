using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public class GameLogic {
		readonly World _world = new World();
		readonly CommandAccessor _commandAccessor = new CommandAccessor();
		readonly VisualStateConverter _visualStateConverter;
		readonly GameLogicContext _context;
		readonly int[] _speedMultipliers;
		readonly ResourceCollectorRegistry _resourceCollectorRegistry;
		readonly string[] _resourceIdUpdateOrder;
		readonly Random _rng;
		readonly Dictionary<string, string> _hqCountryByOrgId;
		readonly Dictionary<string, (double Lon, double Lat)> _provinceCenters;
		readonly ICompletionCondition _completionCondition;
		readonly ProvinceTopology _provinceTopology;
		int _gameTimeEntity = -1;
		int _localeEntity = -1;
		int _settingsEntity = -1;
		int _orgEntity = -1;
		int _proximityEntity = -1;
		int _provinceSelectionEntity = -1;
		int _botActionLogEntity = -1;
		int _gameCompletionEntity = -1;
		int _botActionLogRetentionCap;
		string _sessionId = Guid.NewGuid().ToString("N");
		DateTime _previousTime;
		ActionConfig _actionConfig = null!;
		EffectConfig _effectConfig = null!;

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }
		public World World => _world;
		public ResourceConfig ResourceConfig { get; private set; } = null!;
		public CountryConfig CountryConfig { get; private set; } = null!;
		public CharacterConfig CharacterConfig { get; private set; } = null!;
		public ActionConfig ActionConfig { get; private set; } = null!;
		public EffectConfig EffectConfig { get; private set; } = null!;
		public ProvinceConfig ProvinceConfig { get; private set; } = null!;
		public GameSettings GameSettings { get; private set; } = null!;
		public IReadOnlyList<BotFeatureConfigEntry> BotFeatures { get; private set; } = null!;
		public int MaxControlPool { get; private set; }
		public bool IsCompleted => _gameCompletionEntity >= 0
			&& _world.Get<GameCompletion>(_gameCompletionEntity).IsCompleted;

		public GameLogic(GameLogicContext context) {
			_context = context;
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;
			_rng = context.RngSeed.HasValue ? new Random(context.RngSeed.Value) : new Random();

			_hqCountryByOrgId = new Dictionary<string, string>();
			foreach (var orgEntry in context.Organization.Load().Organizations) {
				_hqCountryByOrgId[orgEntry.OrganizationId] = orgEntry.HqCountryId;
			}

			ResourceConfig = context.Resource.Load();
			CountryConfig = context.Country.Load();
			CharacterConfig = context.Character.Load();
			ActionConfig = context.Action.Load();
			_actionConfig = ActionConfig;
			_effectConfig = context.Effect.Load();
			EffectConfig = _effectConfig;
			ProvinceConfig = context.Province.Load();
			_provinceCenters = new Dictionary<string, (double Lon, double Lat)>();
			foreach (var entry in ProvinceConfig.Provinces) {
				_provinceCenters[entry.ProvinceId] = (entry.CentroidX, entry.CentroidY);
			}
			_provinceTopology = new ProvinceTopology(ProvinceConfig);
			var settings = context.GameSettings.Load();
			GameSettings = settings;
			settings.WarBattles.Validate();
			_visualStateConverter = new VisualStateConverter(VisualState, _actionConfig, _hqCountryByOrgId,
				settings.GameLog.IncludePlayerActions, settings.GameLog.MaxLogEntries, CountryConfig,
				settings.EventNotifications, settings.CompletionCondition, settings.MaxControlPool, _effectConfig);
			_speedMultipliers = settings.SpeedMultipliers;
			var combatBasesByCountryId = new Dictionary<string, CountryCombatBases>();
			foreach (var entry in CountryConfig.Countries) {
				combatBasesByCountryId[entry.CountryId] = new CountryCombatBases(entry.BaseDamage, entry.BaseDurability);
			}
			_resourceCollectorRegistry = ResourceCollectorRegistry.CreateDefault(
				settings.PopulationGrowthPercentPerMonth, settings.CountryScoreCoefficient,
				settings.RecruitsInitialPercent, settings.RecruitsCapPercent, settings.RecruitsMonthlyIncreasePercent,
				combatBasesByCountryId);
			_resourceIdUpdateOrder = settings.ResourceIdUpdateOrder;
			_botActionLogRetentionCap = settings.BotActionLogRetentionCap;
			BotFeatures = settings.BotFeatures;
			MaxControlPool = settings.MaxControlPool;
			_completionCondition = CompletionConditionFactory.Create(settings.CompletionCondition, MaxControlPool);
			VisualState.SelectedCountry.Control.PoolSize = MaxControlPool;
			_previousTime = new DateTime(settings.StartYear, 1, 1);
		}

		public void Update(float deltaTime) {
			if (InitSystem.Update(_world, _context, _rng)) {
				RefreshSingletonEntities();
				ProvinceOwnershipSystem.Seed(_world, ProvinceConfig);
				ProvinceOccupationSystem.Seed(_world, ProvinceConfig);
			}

			if (IsCompleted) {
				ProcessSaveCommands();
				_commandAccessor.Clear();
				return;
			}

			ref GameTime time = ref _world.Get<GameTime>(_gameTimeEntity);
			_previousTime = time.CurrentTime;

			TimeSystem.Update(
				_world,
				_gameTimeEntity,
				deltaTime,
				_speedMultipliers,
				_commandAccessor.ReadPauseCommand(),
				_commandAccessor.ReadUnpauseCommand(),
				_commandAccessor.ReadChangeTimeMultiplierCommand());

			DateTime currentTime = _world.Get<GameTime>(_gameTimeEntity).CurrentTime;
			ResourceSystem.Update(
				_world, _previousTime, currentTime, _resourceCollectorRegistry, _resourceIdUpdateOrder, ResourceConfig);
			ControlSystem.Update(_world, _previousTime, currentTime);
			// Game Log: sweep last tick's WarResolvedApplied before TryResolvePeaceByChance/the
			// debug StopWar handler (below) might create a new one this tick. See
			// Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
			CleanupEffectNotificationsSystem.UpdateWarResolved(_world);
			Wars.TryResolvePeaceByChance(
				_world, _previousTime, currentTime, _rng, GameSettings, _provinceTopology, _provinceCenters, MaxControlPool, CountryConfig);
			WarSystem.Update(
				_world, _previousTime, currentTime, GameSettings.AttackerWarProgressDecayPerMonth, ResourceConfig);
			RevengeWarBonusDecaySystem.Update(
				_world,
				_previousTime,
				currentTime,
				GameSettings.RevengeDamageBonusDecayPerMonth,
				GameSettings.RevengeDurabilityBonusDecayPerMonth);

			foreach (var cmd in _commandAccessor.ReadChangeControlCommand().AsSpan()) {
				ApplyChangeControl(cmd.OrgId, cmd.CountryId, cmd.Delta);
			}

			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			LocaleSystem.Update(_world, _localeEntity, _commandAccessor.ReadChangeLocaleCommand());
			foreach (var cmd in _commandAccessor.ReadChangeLensCommand().AsSpan()) {
				VisualState.MapLens.Set(cmd.Lens);
			}
			ChangeAutoSaveIntervalSystem.Update(_world, _settingsEntity, _commandAccessor.ReadChangeAutoSaveIntervalCommand());

			if (_context.Storage != null && _context.Serializer != null) {
				AutoSaveSystem.Update(_world, _settingsEntity, _gameTimeEntity, _previousTime, _commandAccessor);
			}

			ProcessSaveCommands();

			// Game Log: sweep last tick's RoleChangeApplied before today's character-cycling
			// handlers might create a new one. See Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
			CleanupEffectNotificationsSystem.UpdateRoleChange(_world);

			foreach (var cmd in _commandAccessor.ReadDebugCycleCharacterCommand().AsSpan()) {
				ApplyDebugCycleCharacter(cmd.OwnerId, cmd.RoleId, cmd.SlotIndex);
			}
			foreach (var cmd in _commandAccessor.ReadDebugDropCharacterCommand().AsSpan()) {
				ApplyDebugDropCharacter(cmd.OwnerId, cmd.RoleId, cmd.SlotIndex);
			}
			foreach (var cmd in _commandAccessor.ReadDebugImproveOpinionCommand().AsSpan()) {
				ApplyDebugImproveOpinion(cmd.CountryId, cmd.OrgId);
			}
			foreach (var cmd in _commandAccessor.ReadDebugChangeGoldCommand().AsSpan()) {
				ApplyDebugChangeGold(cmd.OrgId, cmd.Amount);
			}
			foreach (var cmd in _commandAccessor.ReadDebugForceCompletionConditionCommand().AsSpan()) {
				ApplyDebugForceCompletionCondition(cmd.TargetOrgId, cmd.ConditionType, cmd.Value);
			}
			foreach (var cmd in _commandAccessor.ReadSelectProvinceCommand().AsSpan()) {
				ApplySelectProvince(cmd.ProvinceId);
			}
			foreach (var cmd in _commandAccessor.ReadDebugChangeProvinceOwnerCommand().AsSpan()) {
				var (changed, oldOwnerId) = ProvinceOwnershipSystem.ChangeOwner(_world, cmd.ProvinceId, cmd.NewOwnerId);
				if (changed) {
					VisualState.ProvinceOwnership.Set(
						VisualState.ProvinceOwnership.OwnerByProvinceId,
						cmd.ProvinceId,
						oldOwnerId,
						cmd.NewOwnerId);
				}
			}
			foreach (var cmd in _commandAccessor.ReadDebugSetProvinceOccupationCommand().AsSpan()) {
				var (changed, oldOccupierId) = ProvinceOccupationSystem.SetOccupier(_world, cmd.ProvinceId, cmd.OccupierId);
				if (changed) {
					VisualState.ProvinceOccupation.Set(
						ProvinceOccupationSystem.GetOccupierByProvinceId(_world),
						cmd.ProvinceId,
						oldOccupierId,
						cmd.OccupierId);
				}
			}
			foreach (var cmd in _commandAccessor.ReadDebugClearProvinceOccupationCommand().AsSpan()) {
				var (changed, oldOccupierId) = ProvinceOccupationSystem.ClearOccupier(_world, cmd.ProvinceId);
				if (changed) {
					VisualState.ProvinceOccupation.Set(
						ProvinceOccupationSystem.GetOccupierByProvinceId(_world),
						cmd.ProvinceId,
						oldOccupierId,
						"");
				}
			}
			foreach (var cmd in _commandAccessor.ReadDebugSetCountryRelationCommand().AsSpan()) {
				CountryRelations.SetRelation(_world, cmd.CountryIdA, cmd.CountryIdB, cmd.Kind);
			}
			foreach (var cmd in _commandAccessor.ReadDebugClearCountryRelationCommand().AsSpan()) {
				CountryRelations.RemoveRelation(_world, cmd.CountryIdA, cmd.CountryIdB);
			}
			foreach (var cmd in _commandAccessor.ReadDebugDeclareWarCommand().AsSpan()) {
				Wars.DeclareWar(
					_world, cmd.AttackerCountryId, cmd.DefenderCountryId, currentTime,
					_provinceTopology, GameSettings.WarBattles);
			}
			foreach (var cmd in _commandAccessor.ReadDebugStopWarCommand().AsSpan()) {
				Wars.StopWar(
					_world, cmd.CountryId, currentTime, _rng, GameSettings, _provinceTopology, _provinceCenters, MaxControlPool, CountryConfig);
			}
			foreach (var cmd in _commandAccessor.ReadDebugDrawCardCommand().AsSpan()) {
				DrawCardSystem.ForceDrawCard(_world, cmd.OrgId, cmd.CountryId, cmd.ActionId, cmd.TargetCountryId);
			}
			foreach (var cmd in _commandAccessor.ReadDebugDiscardCardCommand().AsSpan()) {
				RemoveCardFromHandSystem.ForceDiscard(
					_world, cmd.OrgId, cmd.CountryId, cmd.ActionId, cmd.TargetCountryId, cmd.SlotIndex);
			}
			CleanupActionEffectsSystem.Update(_world);
			// War battles: sweep last tick's ResourceChange before WarBattleSystem creates this
			// tick's battle-caused ResourceChange, so VisualStateConverter (below) sees it once,
			// same as the card pipeline's DeductActionCostSystem/CreateActionEffectSystem.
			WarBattleSystem.Update(
				_world, _previousTime, currentTime, _rng, _provinceTopology, GameSettings.WarBattles, ResourceConfig);

			// Game Log: sweep last tick's Control/Opinion events before
			// CreateActionEffectSystem creates this tick's batch below.
			// See Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
			CleanupEffectNotificationsSystem.UpdateActionEffects(_world);
			InitActionFromPlayCardSystem.Update(_world, _commandAccessor.ReadPlayCardActionCommand());
			CheckActionConditionSystem.Update(_world, _actionConfig, _hqCountryByOrgId);
			DeductActionCostSystem.Update(_world, _actionConfig);
			ActionSucceededSystem.Update(_world, _actionConfig);
			bool hasSucceededCardActions = HasSucceededCardActions(_world);
			CreateActionEffectSystem.Update(
				_world, _actionConfig, _effectConfig, currentTime,
				_rng, GameSettings, _provinceTopology, _provinceCenters, MaxControlPool, _hqCountryByOrgId, CountryConfig);
			// A succeeded card can grant a CountryResourceModifier effect (e.g. sell_arms'
			// troops_damage_bonus_percent) that Damage/Durability's daily-gated collectors
			// won't pick up until the next day boundary — settle immediately so the War
			// Progress window reflects the change the same tick it was played.
			if (hasSucceededCardActions) {
				SettleCombatResources();
			}
			SetCountryRelationSystem.Update(_world, _proximityEntity, _rng);
			ClearCountryRelationSystem.Update(_world);
			RemoveCardFromHandSystem.Update(_world);
			CheckHandSizeSystem.Update(_world);
			RelationCardSyncSystem.Update(_world, _actionConfig);
			RevengeCardSyncSystem.Update(_world, _actionConfig);
			DrawCardSystem.Update(_world, _actionConfig, _rng, _hqCountryByOrgId);
			CleanupCardDiscardSystem.Update(_world);
			GameCompletionSystem.Update(_world, _gameCompletionEntity, _completionCondition, MaxControlPool);

			_commandAccessor.Clear();
			_visualStateConverter.Update(deltaTime, _world, _gameTimeEntity, _localeEntity, _orgEntity);
		}

		public void LoadState(string saveName) {
			if (_context.Storage == null || _context.Serializer == null) {
				return;
			}
			string json = _context.Storage.Read($"Saves/{saveName}.json");
			var snapshot = _context.Serializer.Deserialize(json);
			LoadSystem.Apply(snapshot, _world);
			_commandAccessor.Clear();
			if (!string.IsNullOrEmpty(snapshot.Header.SessionId)) {
				_sessionId = snapshot.Header.SessionId;
			}
			RefreshSingletonEntities();
			ReconcileLoadedCompletionState();
			RefreshSingletonEntities();
			_previousTime = _world.Get<GameTime>(_gameTimeEntity).CurrentTime;
			GameCompletionSystem.Update(_world, _gameCompletionEntity, _completionCondition, MaxControlPool);
			SettleCombatResources();
			_visualStateConverter.Update(0f, _world, _gameTimeEntity, _localeEntity, _orgEntity);
		}

		void ReconcileLoadedCompletionState() {
			if (_gameCompletionEntity < 0) {
				_gameCompletionEntity = _world.Create();
				_world.Add(_gameCompletionEntity, new GameCompletion {
					IsCompleted = false,
					WinnerOrganizationId = ""
				});
			}

			var organizations = new Dictionary<string, int>(StringComparer.Ordinal);
			var savedOrders = new HashSet<int>();
			var missingOutcomeIds = new HashSet<string>(StringComparer.Ordinal);
			int[] required = { TypeId<Organization>.Value };
			foreach (Archetype archetype in _world.GetMatchingArchetypes(required, null)) {
				Organization[] organizationComponents = archetype.GetColumn<Organization>();
				for (int i = 0; i < archetype.Count; i++) {
					string organizationId = organizationComponents[i].OrganizationId;
					int entity = archetype.Entities[i];
					if (!organizations.TryAdd(organizationId, entity)) {
						throw new InvalidOperationException(
							$"Cannot reconcile loaded completion state: duplicate organization ID '{organizationId}'.");
					}

					if (_world.Has<OrganizationGameOutcome>(entity)) {
						int order = _world.Get<OrganizationGameOutcome>(entity).ParticipationOrder;
						if (order < 0 || !savedOrders.Add(order)) {
							throw new InvalidOperationException(
								$"Cannot reconcile loaded completion state: participation order {order} is invalid or duplicated.");
						}
					} else {
						missingOutcomeIds.Add(organizationId);
					}
				}
			}

			var reconstructionOrder = new List<string>();
			if (_context.ParticipatingOrganizationIds != null && _context.ParticipatingOrganizationIds.Count > 0) {
				foreach (string organizationId in _context.ParticipatingOrganizationIds) {
					if (missingOutcomeIds.Contains(organizationId) && !reconstructionOrder.Contains(organizationId)) {
						reconstructionOrder.Add(organizationId);
					}
				}
			} else if (!string.IsNullOrEmpty(_context.InitialOrganizationId)
				&& missingOutcomeIds.Contains(_context.InitialOrganizationId)) {
				reconstructionOrder.Add(_context.InitialOrganizationId);
			}

			var unmatched = new List<string>(missingOutcomeIds);
			unmatched.Sort(StringComparer.Ordinal);
			foreach (string organizationId in unmatched) {
				if (!reconstructionOrder.Contains(organizationId)) {
					reconstructionOrder.Add(organizationId);
				}
			}

			int nextOrder = 0;
			foreach (string organizationId in reconstructionOrder) {
				while (savedOrders.Contains(nextOrder)) {
					nextOrder++;
				}
				_world.Add(organizations[organizationId], new OrganizationGameOutcome {
					ParticipationOrder = nextOrder,
					Result = OrganizationGameResult.InProgress
				});
				savedOrders.Add(nextOrder);
				nextOrder++;
			}
		}

		void SaveGame(bool isAutoSave) {
			if (_context.Storage == null || _context.Serializer == null) {
				return;
			}
			try {
				var snapshot = SaveSystem.BuildSnapshot(_world);
				snapshot.Header.SessionId = _sessionId;
				string fileName = isAutoSave ? $"autosave_{snapshot.Header.OrganizationId}_{_sessionId}" : snapshot.Header.SaveName;
				_context.Storage.Write(
					$"Saves/{fileName}.json",
					_context.Serializer.Serialize(snapshot));
				if (!isAutoSave) {
					_context.Logger?.LogInfo($"[FlyText] SaveGame succeeded, fileName={fileName}");
					VisualState.SaveResult.Set(true, null);
				}
			} catch (Exception ex) {
				_context.Logger?.LogError($"[FlyText] SaveGame threw: {ex}");
				if (!isAutoSave) {
					VisualState.SaveResult.Set(false, ex.GetType().Name);
				}
			}
		}

		void ProcessSaveCommands() {
			var saveCommands = _commandAccessor.ReadSaveGameCommand();
			if (saveCommands.Count == 0) {
				return;
			}

			bool isAutoSave = false;
			foreach (var cmd in saveCommands.AsSpan()) {
				isAutoSave = cmd.IsAutoSave;
			}
			SaveGame(isAutoSave);
		}

		void RefreshSingletonEntities() {
			_gameTimeEntity = FindEntityWith<GameTime>();
			_localeEntity = FindEntityWith<Locale>();
			_settingsEntity = FindEntityWith<AppSettings>();
			_orgEntity = FindViewOrgEntity();
			_proximityEntity = FindEntityWith<ProximityMapData>();
			_provinceSelectionEntity = FindEntityWith<ProvinceSelection>();
			_botActionLogEntity = FindEntityWith<BotActionLog>();
			_gameCompletionEntity = FindEntityWith<GameCompletion>();
		}

		int FindViewOrgEntity() {
			int fallback = -1;
			if (!string.IsNullOrEmpty(_context.InitialOrganizationId)) {
				int[] req = { TypeId<Organization>.Value };
				foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
					Organization[] orgs = arch.GetColumn<Organization>();
					for (int i = 0; i < arch.Count; i++) {
						if (fallback < 0) { fallback = arch.Entities[i]; }
						if (orgs[i].OrganizationId == _context.InitialOrganizationId) {
							return arch.Entities[i];
						}
					}
				}
				return fallback;
			}
			return FindEntityWith<Organization>();
		}

		public void RebuildProximityMap() {
			InitSystem.BuildProximityMap(_world, _context);
			_proximityEntity = FindEntityWith<ProximityMapData>();
		}

		public void RecordBotAction(string orgId, string featureId, string actionId, string countryId) {
			if (IsCompleted || _botActionLogEntity < 0) { return; }
			DateTime date = _gameTimeEntity >= 0 ? _world.Get<GameTime>(_gameTimeEntity).CurrentTime : default;
			string dateStr = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
			string target = string.IsNullOrEmpty(countryId) ? "" : $" -> {countryId}";
			string record = $"{dateStr} | {orgId} | {featureId}/{actionId}{target}";
			ref BotActionLog log = ref _world.Get<BotActionLog>(_botActionLogEntity);
			var existing = log.Entries ?? Array.Empty<string>();
			var appended = new string[existing.Length + 1];
			Array.Copy(existing, appended, existing.Length);
			appended[existing.Length] = record;
			if (_botActionLogRetentionCap > 0 && appended.Length > _botActionLogRetentionCap) {
				int overflow = appended.Length - _botActionLogRetentionCap;
				var trimmed = new string[_botActionLogRetentionCap];
				Array.Copy(appended, overflow, trimmed, 0, _botActionLogRetentionCap);
				appended = trimmed;
			}
			log.Entries = appended;
		}

		int FindEntityWith<T>() {
			int[] required = { TypeId<T>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.Entities[0];
				}
			}
			return -1;
		}

		void ApplyChangeControl(string orgId, string countryId, int delta) {
			ControlSystem.ApplyChangeControl(_world, orgId, countryId, delta, MaxControlPool);
		}

		void ApplyDebugCycleCharacter(string ownerId, string roleId, int slotIndex) {
			if (IsOrgOwner(ownerId)) {
				CycleOrgCharacterSlot(ownerId, roleId, slotIndex);
			} else {
				CycleCountryCharacter(ownerId, roleId);
			}
		}

		void CycleOrgCharacterSlot(string orgId, string roleId, int slotIndex) {
			var pool = CharacterConfig.FindOrgPool(orgId);
			if (pool == null || !pool.Slots.TryGetValue(roleId, out var candidates) || candidates.Count == 0) {
				return;
			}

			int slotEntityId = FindCharacterSlotEntity(orgId, roleId, slotIndex);
			if (slotEntityId < 0) { return; }

			ref CharacterSlot slot = ref _world.Get<CharacterSlot>(slotEntityId);
			string currentCharId = slot.CharacterId;

			int currentIdx = -1;
			for (int i = 0; i < candidates.Count; i++) {
				if (candidates[i].CharacterId == currentCharId) { currentIdx = i; break; }
			}
			int nextIdx = (currentIdx + 1) % candidates.Count;
			var nextEntry = candidates[nextIdx];

			if (!string.IsNullOrEmpty(currentCharId)) {
				RemoveCharacterEntity(currentCharId);
			}

			CreateOrgCharacterEntity(_world, CharacterConfig, _rng, orgId, roleId, nextEntry);

			slot.CharacterId = nextEntry.CharacterId;
			slot.IsAvailable = false;
			// Game Log event — see Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
			_world.Add(_world.Create(), new RoleChangeApplied { OrgId = orgId, CountryId = "", RoleId = roleId, CharacterId = nextEntry.CharacterId });
		}

		void CycleCountryCharacter(string countryId, string roleId) {
			var pool = CharacterConfig.FindPool(countryId);
			if (pool == null || !pool.Slots.TryGetValue(roleId, out var candidates) || candidates.Count == 0) {
				return;
			}

			string currentCharId = FindCountryCharacterId(countryId, roleId);

			int currentIdx = -1;
			for (int i = 0; i < candidates.Count; i++) {
				if (candidates[i].CharacterId == currentCharId) { currentIdx = i; break; }
			}
			int nextIdx = (currentIdx + 1) % candidates.Count;
			var nextEntry = candidates[nextIdx];

			if (!string.IsNullOrEmpty(currentCharId)) {
				RemoveCharacterEntity(currentCharId);
			}

			int charEntity = _world.Create();
			var namePartKeys = new string[nextEntry.NamePartKeys.Count];
			for (int i = 0; i < nextEntry.NamePartKeys.Count; i++) {
				namePartKeys[i] = nextEntry.NamePartKeys[i];
			}
			_world.Add(charEntity, new Character {
				CharacterId = nextEntry.CharacterId,
				CountryId = countryId,
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = namePartKeys
			});
			foreach (var skillDef in CharacterConfig.Skills) {
				int sv;
				if (nextEntry.Skills.TryGetValue(skillDef.SkillId, out var ss)) {
					sv = _rng.Next(ss.MinValue, ss.MaxValue + 1);
				} else {
					sv = _rng.Next(5, 31);
				}
				int se = _world.Create();
				_world.Add(se, new ResourceOwner(nextEntry.CharacterId, OwnerType.Character));
				_world.Add(se, new Resource { ResourceId = skillDef.SkillId, Value = sv });
			}
			// Game Log event — see Docs/Specs/26_07_18_07_action-log-ui/plan.md ordering note.
			_world.Add(_world.Create(), new RoleChangeApplied { OrgId = "", CountryId = countryId, RoleId = roleId, CharacterId = nextEntry.CharacterId });
			if (IsWarRelevantRole(roleId)) {
				SettleCombatResources();
			}
		}

		void ApplyDebugDropCharacter(string ownerId, string roleId, int slotIndex) {
			bool isOrg = IsOrgOwner(ownerId);
			bool isPlayerOwner = isOrg && _orgEntity >= 0
				? ownerId == _world.Get<Organization>(_orgEntity).OrganizationId
				: false;

			if (isOrg) {
				int slotEntityId = FindCharacterSlotEntity(ownerId, roleId, slotIndex);
				if (slotEntityId < 0) { return; }
				ref CharacterSlot slot = ref _world.Get<CharacterSlot>(slotEntityId);
				if (!string.IsNullOrEmpty(slot.CharacterId)) {
					RemoveCharacterEntity(slot.CharacterId);
					slot.CharacterId = "";
				}
				slot.IsAvailable = isPlayerOwner;
			} else {
				string charId = FindCountryCharacterId(ownerId, roleId);
				if (!string.IsNullOrEmpty(charId)) {
					RemoveCharacterEntity(charId);
					if (IsWarRelevantRole(roleId)) {
						SettleCombatResources();
					}
				}
			}
		}

		void ApplyDebugImproveOpinion(string countryId, string orgId) {
			string opinionResourceId = $"opinion_{orgId}";
			int[] charReq = { TypeId<Character>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(charReq, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (chars[i].CountryId != countryId) { continue; }
					string charId = chars[i].CharacterId;
					int[] resReq = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
					bool found = false;
					foreach (var resArch in _world.GetMatchingArchetypes(resReq, null)) {
						ResourceOwner[] owners = resArch.GetColumn<ResourceOwner>();
						Resource[] resources = resArch.GetColumn<Resource>();
						int rc = resArch.Count;
						for (int j = 0; j < rc; j++) {
							if (owners[j].OwnerId == charId && resources[j].ResourceId == opinionResourceId) {
								resources[j].Value = Math.Min(100, resources[j].Value + 50);
								found = true;
								break;
							}
						}
						if (found) { break; }
					}
					if (!found) {
						int re = _world.Create();
						_world.Add(re, new ResourceOwner(charId, OwnerType.Character));
						_world.Add(re, new Resource { ResourceId = opinionResourceId, Value = 50 });
					}
				}
			}
		}

		void ApplySelectProvince(string provinceId) {
			if (_provinceSelectionEntity < 0) {
				_provinceSelectionEntity = _world.Create();
				_world.Add(_provinceSelectionEntity, new ProvinceSelection { ProvinceId = provinceId });
			} else {
				ref ProvinceSelection selection = ref _world.Get<ProvinceSelection>(_provinceSelectionEntity);
				selection.ProvinceId = provinceId;
			}
		}

		void ApplyDebugChangeGold(string orgId, double amount) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].OwnerId == orgId && resources[i].ResourceId == ResourceDefinitions.Gold) {
						resources[i].Value = System.Math.Max(0, resources[i].Value + amount);
						return;
					}
				}
			}
		}

		// Debug-only completion forcer: pushes a target org over a single flattened
		// completion-condition leaf (see WinConditionHintProjector for the same flattening
		// used to label these debug buttons). Reduces the most-control opponent(s) in a
		// country first to free room before granting the target org control there, so it
		// never silently no-ops when opponents already occupy the country's control pool.
		void ApplyDebugForceCompletionCondition(string targetOrgId, string conditionType, double value) {
			_context.Logger?.LogDebug($"[DebugForceCompletion] received: target='{targetOrgId}' conditionType='{conditionType}' value={value}");
			if (string.IsNullOrEmpty(targetOrgId) || !CompletionConditionTypeParser.TryParse(conditionType, out var type)) {
				_context.Logger?.LogDebug($"[DebugForceCompletion] aborted: invalid target or conditionType='{conditionType}'");
				return;
			}

			var countryIds = new List<string>(GameCompletionSystem.GetAvailableCountryIds(_world));
			if (countryIds.Count == 0) {
				_context.Logger?.LogDebug("[DebugForceCompletion] aborted: no available countries");
				return;
			}
			countryIds.Sort(StringComparer.Ordinal);

			switch (type) {
				case CompletionConditionType.TotalControl:
					ForceTotalControl(targetOrgId, value, countryIds);
					break;
				case CompletionConditionType.FullControlCountries:
					ForceFullControlCountries(targetOrgId, (int)value, countryIds);
					break;
				case CompletionConditionType.ScoreGoal:
					_context.Logger?.LogError($"[DebugForceCompletion] conditionType='score_goal' is not supported by this debug command, no-op: target='{targetOrgId}' value={value}");
					break;
			}

			SettleOrgScores($"target='{targetOrgId}' conditionType='{conditionType}' value={value}");
			_context.Logger?.LogDebug($"[DebugForceCompletion] done: target='{targetOrgId}' IsCompleted={IsCompleted}");
		}

		void ForceTotalControl(string targetOrgId, double threshold, List<string> countryIds) {
			int totalCapacity = countryIds.Count * MaxControlPool;
			int requiredTotal = (int)Math.Ceiling(threshold * totalCapacity - 1e-9);

			Dictionary<string, int> controlByCountry = OrgMetrics.GetControlByCountry(_world, targetOrgId, countryIds);
			int currentTotal = 0;
			foreach (int v in controlByCountry.Values) { currentTotal += v; }
			int remaining = requiredTotal - currentTotal;
			_context.Logger?.LogDebug($"[DebugForceCompletion] total_control: threshold={threshold} requiredTotal={requiredTotal} currentTotal={currentTotal} remaining={remaining}");
			if (remaining <= 0) {
				_context.Logger?.LogDebug("[DebugForceCompletion] total_control: already satisfied, no-op");
				return;
			}

			foreach (string countryId in countryIds) {
				if (remaining <= 0) { break; }
				controlByCountry.TryGetValue(countryId, out int targetHere);
				int addHere = Math.Min(remaining, MaxControlPool - targetHere);
				if (addHere <= 0) { continue; }
				ForceControlInCountry(targetOrgId, countryId, addHere);
				remaining -= addHere;
			}
		}

		void ForceFullControlCountries(string targetOrgId, int requiredCountryCount, List<string> countryIds) {
			Dictionary<string, int> controlByCountry = OrgMetrics.GetControlByCountry(_world, targetOrgId, countryIds);
			int currentFullCount = 0;
			foreach (int v in controlByCountry.Values) {
				if (v >= MaxControlPool) { currentFullCount++; }
			}
			int neededCountries = requiredCountryCount - currentFullCount;
			_context.Logger?.LogDebug($"[DebugForceCompletion] full_control_countries: required={requiredCountryCount} currentFullCount={currentFullCount} neededCountries={neededCountries}");
			if (neededCountries <= 0) {
				_context.Logger?.LogDebug("[DebugForceCompletion] full_control_countries: already satisfied, no-op");
				return;
			}

			foreach (string countryId in countryIds) {
				if (neededCountries <= 0) { break; }
				controlByCountry.TryGetValue(countryId, out int targetHere);
				if (targetHere >= MaxControlPool) { continue; }
				ForceControlInCountry(targetOrgId, countryId, MaxControlPool - targetHere);
				neededCountries--;
			}
		}

		// Frees room by deducting from the most-control opponent(s) in the country first
		// (ties broken ordinally for determinism), then grants the target org's increment.
		void ForceControlInCountry(string targetOrgId, string countryId, int needed) {
			if (needed <= 0) { return; }
			int freeRoom = MaxControlPool - GetTotalControlInCountry(countryId);
			if (freeRoom < needed) {
				int deficit = needed - freeRoom;
				foreach (var (opponentOrgId, opponentValue) in GetOtherOrgsControlDescending(countryId, targetOrgId)) {
					if (deficit <= 0) { break; }
					int reduceBy = Math.Min(opponentValue, deficit);
					// Not ApplyChangeControl: an org's control in a country can span multiple
					// ControlEffect entities (e.g. its own HQ's "base_{orgId}" seed effect
					// alongside a "permanent_{orgId}_{countryId}" one from prior gameplay).
					// ApplyChangeControl only ever touches the "permanent_" one, which would
					// silently under-evict — this reduces across all of the org's effects here.
					ReduceOrgControlInCountry(opponentOrgId, countryId, reduceBy);
					deficit -= reduceBy;
					_context.Logger?.LogDebug($"[DebugForceCompletion] evicted '{opponentOrgId}' by {reduceBy} in '{countryId}' to make room for '{targetOrgId}'");
				}
			}
			ApplyChangeControl(targetOrgId, countryId, needed);
			_context.Logger?.LogDebug($"[DebugForceCompletion] granted '{targetOrgId}' +{needed} control in '{countryId}'");
		}

		// Reduces total ControlEffect value across ALL of orgId's effect entities in
		// countryId (not just the "permanent_" one) by `amount`, deterministically ordered
		// by EffectId. Entries are collected before mutating — Destroy/Get are structural or
		// row-level changes that must not run while GetMatchingArchetypes is still enumerating.
		void ReduceOrgControlInCountry(string orgId, string countryId, int amount) {
			if (amount <= 0) { return; }
			var entries = new List<(int Entity, string EffectId, int Value)>();
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int[] entities = arch.Entities;
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].OrgId == orgId && effects[i].CountryId == countryId) {
						entries.Add((entities[i], effects[i].EffectId, effects[i].Value));
					}
				}
			}
			entries.Sort((a, b) => string.CompareOrdinal(a.EffectId, b.EffectId));

			int remaining = amount;
			foreach (var (entity, _, value) in entries) {
				if (remaining <= 0) { break; }
				int reduceBy = Math.Min(value, remaining);
				int newVal = value - reduceBy;
				if (newVal <= 0) {
					_world.Destroy(entity);
				} else {
					ref ControlEffect fx = ref _world.Get<ControlEffect>(entity);
					fx.Value = newVal;
				}
				remaining -= reduceBy;
			}
		}

		static bool IsWarRelevantRole(string roleId) {
			return roleId == "ruler" || roleId == "military_advisor" || roleId == "economic_advisor";
		}

		static bool HasSucceededCardActions(World world) {
			int[] required = { TypeId<GameAction>.Value, TypeId<ActionSucceeded>.Value, TypeId<CardUse>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return true;
				}
			}
			return false;
		}

		// Cycle runs after the first ResourceSystem pass, so a same-tick settle is required.
		// Instant seed applies without ForceResourceRecompute. Resolve Instant first, then mark
		// Daily and re-run: Absolute collectors compute target - currentValue, so forcing Daily in
		// the same ResolveCollectors pass as Instant (both seeing currentValue 0) would double.
		void SettleCombatResources() {
			DateTime now = _gameTimeEntity >= 0 ? _world.Get<GameTime>(_gameTimeEntity).CurrentTime : _previousTime;
			ResourceSystem.Update(_world, now, now, _resourceCollectorRegistry, _resourceIdUpdateOrder);
			MarkResourceEffectsForRecompute(ResourceDefinitions.Damage);
			MarkResourceEffectsForRecompute(ResourceDefinitions.Durability);
			ResourceSystem.Update(_world, now, now, _resourceCollectorRegistry, _resourceIdUpdateOrder);
		}

		// Debug-only score settle: org_score is a Daily-gated collector-driven resource
		// (OrgScoreCollector), so it only recomputes from live ControlEffect state on a real
		// day-boundary tick. A debug-forced control change doesn't advance GameTime, and once
		// GameCompletionSystem completes the game this same tick, GameLogic.Update never runs
		// again — so without this, org_score would stay permanently stale at its pre-force
		// value. Marks every org_score effect entity for an out-of-band recompute (bypassing
		// the Daily gate via ForceResourceRecompute) and re-runs ResourceSystem.Update with
		// previousTime == currentTime so no other Monthly/Daily effect double-applies.
		void SettleOrgScores(string logContext) {
			var orgIds = new List<string>();
			int[] orgReq = { TypeId<Organization>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(orgReq, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				for (int i = 0; i < arch.Count; i++) { orgIds.Add(orgs[i].OrganizationId); }
			}

			var before = new Dictionary<string, double>();
			foreach (string orgId in orgIds) {
				before[orgId] = ResourceQuery.GetValue(_world, orgId, ResourceDefinitions.OrgScore);
			}

			MarkResourceEffectsForRecompute(ResourceDefinitions.OrgScore);

			DateTime now = _gameTimeEntity >= 0 ? _world.Get<GameTime>(_gameTimeEntity).CurrentTime : _previousTime;
			ResourceSystem.Update(_world, now, now, _resourceCollectorRegistry, _resourceIdUpdateOrder);

			foreach (string orgId in orgIds) {
				double after = ResourceQuery.GetValue(_world, orgId, ResourceDefinitions.OrgScore);
				_context.Logger?.LogDebug($"[DebugForceCompletion] {logContext}: org_score settled for '{orgId}': {before[orgId]:0.###} -> {after:0.###}");
			}
		}

		void MarkResourceEffectsForRecompute(string resourceId) {
			// Collect matching entities first: World.Add is a structural change (moves the
			// entity to a different archetype) and must not run while GetMatchingArchetypes'
			// enumerator is still walking the archetype list it would mutate.
			// Skip Instant: ForceResourceRecompute is redundant for Instant (it always applies).
			// Skipping Instant here does **not** prevent Instant+Daily doubling — Instant still
			// applies in the same ResolveCollectors pass as a forced Daily. Absolute Instant+Daily
			// pairs (damage/durability) must use SettleCombatResources' two-pass
			// Update (Instant first, then mark Daily, then Update again).
			var toMark = new List<int>();
			int[] req = { TypeId<ResourceLink>.Value, TypeId<ResourceEffect>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int[] entities = arch.Entities;
				for (int i = 0; i < arch.Count; i++) {
					if (links[i].ResourceId != resourceId) { continue; }
					if (effects[i].PayType == PayType.Instant) { continue; }
					if (!_world.Has<ForceResourceRecompute>(entities[i])) {
						toMark.Add(entities[i]);
					}
				}
			}
			foreach (int entity in toMark) {
				_world.Add(entity, new ForceResourceRecompute());
			}
		}

		int GetTotalControlInCountry(string countryId) {
			int total = 0;
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].CountryId == countryId) { total += effects[i].Value; }
				}
			}
			return total;
		}

		List<(string OrgId, int Value)> GetOtherOrgsControlDescending(string countryId, string excludeOrgId) {
			var byOrg = new Dictionary<string, int>();
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].CountryId != countryId || effects[i].OrgId == excludeOrgId) { continue; }
					byOrg.TryGetValue(effects[i].OrgId, out int existing);
					byOrg[effects[i].OrgId] = existing + effects[i].Value;
				}
			}
			var list = new List<(string OrgId, int Value)>();
			foreach (var kv in byOrg) { list.Add((kv.Key, kv.Value)); }
			list.Sort((a, b) => {
				int cmp = b.Value.CompareTo(a.Value);
				return cmp != 0 ? cmp : string.CompareOrdinal(a.OrgId, b.OrgId);
			});
			return list;
		}

		bool IsOrgOwner(string ownerId) {
			int[] req = { TypeId<Organization>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				for (int i = 0; i < arch.Count; i++) {
					if (orgs[i].OrganizationId == ownerId) { return true; }
				}
			}
			return false;
		}

		int FindCharacterSlotEntity(string ownerId, string roleId, int slotIndex) {
			int[] req = { TypeId<CharacterSlot>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				CharacterSlot[] slots = arch.GetColumn<CharacterSlot>();
				for (int i = 0; i < arch.Count; i++) {
					if (slots[i].OwnerId == ownerId && slots[i].RoleId == roleId && slots[i].SlotIndex == slotIndex) {
						return arch.Entities[i];
					}
				}
			}
			return -1;
		}

		string FindCountryCharacterId(string countryId, string roleId) {
			int[] req = { TypeId<Character>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				Character[] chars = arch.GetColumn<Character>();
				for (int i = 0; i < arch.Count; i++) {
					if (chars[i].CountryId == countryId && chars[i].RoleId == roleId) {
						return chars[i].CharacterId;
					}
				}
			}
			return "";
		}

		void RemoveCharacterEntity(string charId) {
			int[] charReq = { TypeId<Character>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(charReq, null)) {
				Character[] chars = arch.GetColumn<Character>();
				for (int i = 0; i < arch.Count; i++) {
					if (chars[i].CharacterId == charId) {
						_world.Destroy(arch.Entities[i]);
						break;
					}
				}
			}
			int[] resReq = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			var toDestroy = new System.Collections.Generic.List<int>();
			foreach (var arch in _world.GetMatchingArchetypes(resReq, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == charId) {
						toDestroy.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int e in toDestroy) { _world.Destroy(e); }
		}

		static void CreateOrgCharacterEntity(World world, CharacterConfig characterConfig, Random rng, string orgId, string roleId, CharacterEntry charEntry) {
			var namePartKeys = new string[charEntry.NamePartKeys.Count];
			for (int i = 0; i < charEntry.NamePartKeys.Count; i++) {
				namePartKeys[i] = charEntry.NamePartKeys[i];
			}
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = charEntry.CharacterId,
				CountryId = "",
				OrgId = orgId,
				RoleId = roleId,
				NamePartKeys = namePartKeys
			});
			var roleDef = characterConfig.FindRole(roleId);
			var roleSkillIds = roleDef != null
				? new System.Collections.Generic.HashSet<string>(roleDef.SkillIds)
				: new System.Collections.Generic.HashSet<string>();
			foreach (var skillDef in characterConfig.Skills) {
				if (!roleSkillIds.Contains(skillDef.SkillId)) { continue; }
				int sv;
				if (charEntry.Skills.TryGetValue(skillDef.SkillId, out var ss)) {
					sv = rng.Next(ss.MinValue, ss.MaxValue + 1);
				} else {
					sv = rng.Next(5, 31);
				}
				int se = world.Create();
				world.Add(se, new ResourceOwner(charEntry.CharacterId, OwnerType.Character));
				world.Add(se, new Resource { ResourceId = skillDef.SkillId, Value = sv });
			}
		}
	}
}
