using System;
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
		readonly Random _rng;
		int _gameTimeEntity = -1;
		int _localeEntity = -1;
		int _settingsEntity = -1;
		int _orgEntity = -1;
		DateTime _previousTime;

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }
		public World World => _world;
		public ResourceConfig ResourceConfig { get; private set; } = null!;
		public CharacterConfig CharacterConfig { get; private set; } = null!;

		public GameLogic(GameLogicContext context) {
			_context = context;
			_visualStateConverter = new VisualStateConverter(VisualState);
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;
			_rng = new Random();

			ResourceConfig = context.Resource.Load();
			CharacterConfig = context.Character.Load();
			var settings = context.GameSettings.Load();
			_speedMultipliers = settings.SpeedMultipliers;
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
			InfluenceSystem.Update(_world, _previousTime, currentTime);

			foreach (var cmd in _commandAccessor.ReadChangeInfluenceCommand().AsSpan()) {
				ApplyChangeInfluence(cmd.OrgId, cmd.CountryId, cmd.Delta);
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

			if (_commandAccessor.ReadSaveGameCommand().Count > 0) {
				SaveGame();
			}

			foreach (var cmd in _commandAccessor.ReadDebugCycleCharacterCommand().AsSpan()) {
				ApplyDebugCycleCharacter(cmd.OwnerId, cmd.RoleId, cmd.SlotIndex);
			}
			foreach (var cmd in _commandAccessor.ReadDebugDropCharacterCommand().AsSpan()) {
				ApplyDebugDropCharacter(cmd.OwnerId, cmd.RoleId, cmd.SlotIndex);
			}

			_commandAccessor.Clear();
			_visualStateConverter.Update(_world, _gameTimeEntity, _localeEntity, _orgEntity);
		}

		public void LoadState(string saveName) {
			if (_context.Storage == null || _context.Serializer == null) {
				return;
			}
			string json = _context.Storage.Read($"Saves/{saveName}.json");
			var snapshot = _context.Serializer.Deserialize(json);
			LoadSystem.Apply(snapshot, _world);
			RefreshSingletonEntities();
		}

		void SaveGame() {
			if (_context.Storage == null || _context.Serializer == null) {
				return;
			}
			var snapshot = SaveSystem.BuildSnapshot(_world);
			_context.Storage.Write(
				$"Saves/{snapshot.Header.SaveName}.json",
				_context.Serializer.Serialize(snapshot));
		}

		void RefreshSingletonEntities() {
			_gameTimeEntity = FindEntityWith<GameTime>();
			_localeEntity = FindEntityWith<Locale>();
			_settingsEntity = FindEntityWith<AppSettings>();
			_orgEntity = FindEntityWith<Organization>();
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

		void ApplyChangeInfluence(string orgId, string countryId, int delta) {
			InfluenceSystem.ApplyChangeInfluence(_world, orgId, countryId, delta);
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
				_world.Add(se, new ResourceOwner(nextEntry.CharacterId));
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
				world.Add(se, new ResourceOwner(charEntry.CharacterId));
				world.Add(se, new Resource { ResourceId = skillDef.SkillId, Value = sv });
			}
		}
	}
}
