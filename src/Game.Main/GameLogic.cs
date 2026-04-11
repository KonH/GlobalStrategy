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
		readonly int _gameTimeEntity;
		readonly int _localeEntity;
		readonly int[] _speedMultipliers;
		DateTime _previousTime;

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }
		public World World => _world;
		public ResourceConfig ResourceConfig { get; private set; } = null!;

		public GameLogic(GameLogicContext context) {
			_visualStateConverter = new VisualStateConverter(VisualState);
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;

			var countryConfig = context.Country.Load();
			ResourceConfig = context.Resource.Load();
			var resourceConfig = ResourceConfig;

			foreach (var entry in countryConfig.Countries) {
				int entity = _world.Create();
				_world.Add(entity, new Country(entry.CountryId));
				if (entry.CountryId == "Russian_Empire") {
					_world.Add(entity, new Player());
				}
				CreateResourceEntities(entry, resourceConfig);
			}

			var settings = context.GameSettings.Load();
			_speedMultipliers = settings.SpeedMultipliers;
			_previousTime = new DateTime(settings.StartYear, 1, 1);
			_gameTimeEntity = _world.Create();
			_world.Add(_gameTimeEntity, new GameTime {
				CurrentTime = _previousTime,
				IsPaused = false,
				MultiplierIndex = 0
			});

			_localeEntity = _world.Create();
			_world.Add(_localeEntity, new Locale { Value = "en" });
		}

		void CreateResourceEntities(CountryEntry entry, ResourceConfig resourceConfig) {
			foreach (var resourceDef in resourceConfig.Resources) {
				double initialValue = resourceDef.DefaultInitialValue;
				foreach (var init in entry.InitialResources) {
					if (init.ResourceId == resourceDef.ResourceId) {
						initialValue = init.Value;
						break;
					}
				}

				int resourceEntity = _world.Create();
				_world.Add(resourceEntity, new ResourceOwner(entry.CountryId));
				_world.Add(resourceEntity, new Resource { ResourceId = resourceDef.ResourceId, Value = initialValue });

				foreach (var effectDef in resourceDef.DefaultEffects) {
					int effectEntity = _world.Create();
					_world.Add(effectEntity, new ResourceOwner(entry.CountryId));
					_world.Add(effectEntity, new ResourceLink(resourceDef.ResourceId));
					_world.Add(effectEntity, new ResourceEffect {
						EffectId = effectDef.EffectId,
						Value = effectDef.Value,
						PayType = Enum.Parse<PayType>(effectDef.PayType, ignoreCase: true)
					});
				}
			}
		}

		public void Update(float deltaTime) {
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

			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			SelectPlayerCountrySystem.Update(_world, _commandAccessor.ReadSelectPlayerCountryCommand());
			LocaleSystem.Update(_world, _localeEntity, _commandAccessor.ReadChangeLocaleCommand());
			_commandAccessor.Clear();
			_visualStateConverter.Update(_world, _gameTimeEntity, _localeEntity);
		}
	}
}
