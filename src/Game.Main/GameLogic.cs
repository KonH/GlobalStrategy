using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
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
		readonly double _populationGrowthPercent;
		readonly double _countryScoreCoefficient;
		readonly Random _rng;
		readonly Dictionary<string, string> _hqCountryByOrgId;
		int _gameTimeEntity = -1;
		int _localeEntity = -1;
		int _settingsEntity = -1;
		int _orgEntity = -1;
		int _proximityEntity = -1;
		int _provinceSelectionEntity = -1;
		string _sessionId = Guid.NewGuid().ToString("N");
		DateTime _previousTime;
		ActionConfig _actionConfig = null!;
		EffectConfig _effectConfig = null!;

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }
		public World World => _world;
		public ResourceConfig ResourceConfig { get; private set; } = null!;
		public CharacterConfig CharacterConfig { get; private set; } = null!;
		public ActionConfig ActionConfig { get; private set; } = null!;
		public EffectConfig EffectConfig { get; private set; } = null!;
		public ProvinceConfig ProvinceConfig { get; private set; } = null!;

		public GameLogic(GameLogicContext context) {
			_context = context;
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;
			_rng = context.RngSeed.HasValue ? new Random(context.RngSeed.Value) : new Random();

			_hqCountryByOrgId = new Dictionary<string, string>();
			foreach (var orgEntry in context.Organization.Load().Organizations) {
				_hqCountryByOrgId[orgEntry.OrganizationId] = orgEntry.HqCountryId;
			}

			ResourceConfig = context.Resource.Load();
			CharacterConfig = context.Character.Load();
			ActionConfig = context.Action.Load();
			_actionConfig = ActionConfig;
			_effectConfig = context.Effect.Load();
			EffectConfig = _effectConfig;
			ProvinceConfig = context.Province.Load();
			_visualStateConverter = new VisualStateConverter(VisualState, _actionConfig);
			var settings = context.GameSettings.Load();
			_speedMultipliers = settings.SpeedMultipliers;
			_populationGrowthPercent = settings.PopulationGrowthPercentPerMonth;
			_countryScoreCoefficient = settings.CountryScoreCoefficient;
			_previousTime = new DateTime(settings.StartYear, 1, 1);
		}

		public void Update(float deltaTime) {
			if (InitSystem.Update(_world, _context, _rng)) {
				RefreshSingletonEntities();
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
			ResourceSystem.Update(_world, _previousTime, currentTime);
			ControlSystem.Update(_world, _previousTime, currentTime);
			ProvincePopulationGrowthSystem.Update(_world, _previousTime, currentTime, _populationGrowthPercent);
			CountryScoreSystem.Update(_world, _previousTime, currentTime, _countryScoreCoefficient);

			foreach (var cmd in _commandAccessor.ReadChangeControlCommand().AsSpan()) {
				ApplyChangeControl(cmd.OrgId, cmd.CountryId, cmd.Delta);
			}

			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			SelectPlayerCountrySystem.Update(_world, _commandAccessor.ReadSelectPlayerCountryCommand());
			LocaleSystem.Update(_world, _localeEntity, _commandAccessor.ReadChangeLocaleCommand());
			foreach (var cmd in _commandAccessor.ReadChangeLensCommand().AsSpan()) {
				VisualState.MapLens.Set(cmd.Lens);
			}
			ChangeAutoSaveIntervalSystem.Update(_world, _settingsEntity, _commandAccessor.ReadChangeAutoSaveIntervalCommand());

			if (_context.Storage != null && _context.Serializer != null) {
				AutoSaveSystem.Update(_world, _settingsEntity, _gameTimeEntity, _previousTime, _commandAccessor);
			}

			var saveCommands = _commandAccessor.ReadSaveGameCommand();
			if (saveCommands.Count > 0) {
				bool isAutoSave = false;
				foreach (var cmd in saveCommands.AsSpan()) {
					isAutoSave = cmd.IsAutoSave;
				}
				SaveGame(isAutoSave);
			}

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
			if (_commandAccessor.ReadDebugDiscoverAllCountriesCommand().Count > 0) {
				ApplyDebugDiscoverAllCountries();
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

			CleanupActionEffectsSystem.Update(_world);
			InitActionFromPlayCardSystem.Update(_world, _commandAccessor.ReadPlayCardActionCommand());
			CheckActionConditionSystem.Update(_world, _actionConfig);
			DeductActionCostSystem.Update(_world, _actionConfig);
			ActionSucceededSystem.Update(_world, _actionConfig);
			CreateActionEffectSystem.Update(_world, _actionConfig, _effectConfig, currentTime);
			string viewOrgId = _orgEntity >= 0 ? _world.Get<Organization>(_orgEntity).OrganizationId : "";
			DiscoverCountrySystem.Update(_world, _proximityEntity, _rng, viewOrgId, _hqCountryByOrgId);
			RemoveCardFromHandSystem.Update(_world);
			CheckHandSizeSystem.Update(_world);
			DrawCardSystem.Update(_world, _actionConfig, _rng);
			CleanupCardDiscardSystem.Update(_world);

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
			if (!string.IsNullOrEmpty(snapshot.Header.SessionId)) {
				_sessionId = snapshot.Header.SessionId;
			}
			RefreshSingletonEntities();
			CountryScoreSystem.Recompute(_world, _countryScoreCoefficient);
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

		void RefreshSingletonEntities() {
			_gameTimeEntity = FindEntityWith<GameTime>();
			_localeEntity = FindEntityWith<Locale>();
			_settingsEntity = FindEntityWith<AppSettings>();
			_orgEntity = FindViewOrgEntity();
			_proximityEntity = FindEntityWith<ProximityMapData>();
			_provinceSelectionEntity = FindEntityWith<ProvinceSelection>();
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
			ControlSystem.ApplyChangeControl(_world, orgId, countryId, delta);
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
					if (owners[i].OwnerId == orgId && resources[i].ResourceId == "gold") {
						resources[i].Value = System.Math.Max(0, resources[i].Value + amount);
						return;
					}
				}
			}
		}

		void ApplyDebugDiscoverAllCountries() {
			string viewOrgId = _orgEntity >= 0 ? _world.Get<Organization>(_orgEntity).OrganizationId : "";
			if (string.IsNullOrEmpty(viewOrgId)) { return; }

			var discovered = new HashSet<string>();
			int[] discReq = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(discReq, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == viewOrgId) { discovered.Add(dcs[i].CountryId); }
				}
			}

			var toDiscover = new List<string>();
			int[] req = { TypeId<Country>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(req, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (!discovered.Contains(countries[i].CountryId)) {
						toDiscover.Add(countries[i].CountryId);
					}
				}
			}
			foreach (string countryId in toDiscover) {
				int entity = _world.Create();
				_world.Add(entity, new DiscoveredCountry { OrgId = viewOrgId, CountryId = countryId });
			}
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
