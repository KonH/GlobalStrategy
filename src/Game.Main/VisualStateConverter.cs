using ECS;
using GS.Game.Components;

namespace GS.Main {
	class VisualStateConverter {
		readonly VisualState _state;

		internal VisualStateConverter(VisualState state) {
			_state = state;
		}

		internal void Update(IReadOnlyWorld world) {
			int[] required = { TypeId<Country>.Value, TypeId<IsSelected>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count == 0) continue;
				Country[] countries = arch.GetColumn<Country>();
				_state.SelectedCountry.Set(true, countries[0].CountryId);
				return;
			}
			_state.SelectedCountry.Set(false, "");
		}
	}
}
