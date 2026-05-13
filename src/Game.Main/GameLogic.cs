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
	}
}
