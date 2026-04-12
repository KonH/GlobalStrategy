using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public class SelectCountryLogic {
		readonly World _world = new World();
		readonly CommandAccessor _commandAccessor = new CommandAccessor();

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }

		public SelectCountryLogic(IConfigSource<GS.Game.Configs.CountryConfig> countryConfig) {
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;

			var config = countryConfig.Load();
			foreach (var entry in config.Countries) {
				int entity = _world.Create();
				_world.Add(entity, new Country(entry.CountryId));
			}
		}

		public void Update() {
			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			_commandAccessor.Clear();
			UpdateVisualState();
		}

		void UpdateVisualState() {
			int[] required = { TypeId<Country>.Value, TypeId<IsSelected>.Value };
			foreach (var arch in _world.GetMatchingArchetypes(required, null)) {
				if (arch.Count == 0) {
					continue;
				}
				VisualState.SelectedCountry.Set(true, arch.GetColumn<Country>()[0].CountryId);
				return;
			}
			VisualState.SelectedCountry.Set(false, "");
		}
	}
}
