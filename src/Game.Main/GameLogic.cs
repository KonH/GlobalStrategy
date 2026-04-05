using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Systems;

namespace GS.Main {
	public class GameLogic {
		readonly World _world = new World();
		readonly CommandAccessor _commandAccessor = new CommandAccessor();
		readonly VisualStateConverter _visualStateConverter;

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
		}

		public void Update(float deltaTime) {
			SelectCountrySystem.Update(_world, _commandAccessor.ReadSelectCountryCommand());
			_commandAccessor.Clear();
			_visualStateConverter.Update(_world);
		}
	}
}
