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
		int _gameTimeEntity;
		int _localeEntity;
		int _settingsEntity;
		int _orgEntity = -1;
		DateTime _previousTime;

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }
		public World World => _world;
		public ResourceConfig ResourceConfig { get; private set; } = null!;

		public GameLogic(GameLogicContext context) {
			_context = context;
			_visualStateConverter = new VisualStateConverter(VisualState);
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;

			var countryConfig = context.Country.Load();
			ResourceConfig = context.Resource.Load();
			var resourceConfig = ResourceConfig;

			foreach (var entry in countryConfig.Countries) {
				if (!entry.IsAvailable) {
					continue;
				}
				int entity = _world.Create();
				_world.Add(entity, new Country(entry.CountryId));
				if (entry.CountryId == context.InitialPlayerCountryId) {
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
			_world.Add(_localeEntity, new Locale { Value = settings.DefaultLocale });

			_settingsEntity = _world.Create();
			_world.Add(_settingsEntity, new AppSettings {
				Locale = settings.DefaultLocale,
				AutoSaveInterval = ParseAutoSaveInterval(settings.AutoSaveInterval)
			});

			var orgConfig = context.Organization.Load();
			var orgEntry = orgConfig.FindById(context.InitialOrganizationId);
			if (orgEntry != null) {
				_orgEntity = _world.Create();
				_world.Add(_orgEntity, new Organization {
					OrganizationId = orgEntry.OrganizationId,
					DisplayName = orgEntry.DisplayName
				});

				int orgGoldEntity = _world.Create();
				_world.Add(orgGoldEntity, new ResourceOwner(orgEntry.OrganizationId));
				_world.Add(orgGoldEntity, new Resource { ResourceId = "gold", Value = orgEntry.InitialGold });

				int influenceEntity = _world.Create();
				_world.Add(influenceEntity, new InfluenceEffect {
					OrgId     = orgEntry.OrganizationId,
					CountryId = orgEntry.HqCountryId,
					Value     = orgEntry.BaseInfluence,
					EffectId  = $"base_{orgEntry.OrganizationId}"
				});
			}
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
			InfluenceSystem.Update(_world, _previousTime, currentTime);

			foreach (var cmd in _commandAccessor.ReadChangeInfluenceCommand().AsSpan()) {
				ApplyChangeInfluence(cmd.OrgId, cmd.CountryId, cmd.Delta);
			}

			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			SelectPlayerCountrySystem.Update(_world, _commandAccessor.ReadSelectPlayerCountryCommand());
			LocaleSystem.Update(_world, _localeEntity, _commandAccessor.ReadChangeLocaleCommand());
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

		static AutoSaveInterval ParseAutoSaveInterval(string value) {
			return value.ToLowerInvariant() switch {
				"daily"   => AutoSaveInterval.Daily,
				"yearly"  => AutoSaveInterval.Yearly,
				_         => AutoSaveInterval.Monthly
			};
		}
	}
}
