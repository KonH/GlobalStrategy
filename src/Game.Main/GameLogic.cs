using System;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Systems;

namespace GS.Main {
	public class GameLogic {
		readonly World _world = new World();
		readonly CommandAccessor _commandAccessor = new CommandAccessor();
		readonly VisualStateConverter _visualStateConverter;
		readonly int _gameTimeEntity;
		readonly int _localeEntity;
		readonly int[] _speedMultipliers;

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }

		public GameLogic(GameLogicContext context) {
			_visualStateConverter = new VisualStateConverter(VisualState);
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;

			var countryConfig = context.Country.Load();
			foreach (var entry in countryConfig.Countries) {
				int entity = _world.Create();
				_world.Add(entity, new Country(entry.CountryId));
			}

			var settings = context.GameSettings.Load();
			_speedMultipliers = settings.SpeedMultipliers;
			_gameTimeEntity = _world.Create();
			_world.Add(_gameTimeEntity, new GameTime {
				CurrentTime = new DateTime(settings.StartYear, 1, 1),
				IsPaused = false,
				MultiplierIndex = 0
			});

			_localeEntity = _world.Create();
			_world.Add(_localeEntity, new Locale { Value = "en" });
		}

		public void Update(float deltaTime) {
			TimeSystem.Update(
				_world,
				_gameTimeEntity,
				deltaTime,
				_speedMultipliers,
				_commandAccessor.ReadPauseCommand(),
				_commandAccessor.ReadUnpauseCommand(),
				_commandAccessor.ReadChangeTimeMultiplierCommand());
			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			LocaleSystem.Update(_world, _localeEntity, _commandAccessor.ReadChangeLocaleCommand());
			_commandAccessor.Clear();
			_visualStateConverter.Update(_world, _gameTimeEntity, _localeEntity);
		}
	}
}
