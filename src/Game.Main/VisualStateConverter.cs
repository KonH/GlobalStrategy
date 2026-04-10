using ECS;
using GS.Game.Components;

namespace GS.Main {
	class VisualStateConverter {
		readonly VisualState _state;

		internal VisualStateConverter(VisualState state) {
			_state = state;
		}

		internal void Update(IReadOnlyWorld world, int gameTimeEntity, int localeEntity) {
			UpdateSelectedCountry(world);
			UpdateTime(world, gameTimeEntity);
			UpdateLocale(world, localeEntity);
		}

		void UpdateSelectedCountry(IReadOnlyWorld world) {
			int[] required = { TypeId<Country>.Value, TypeId<IsSelected>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count == 0) {
					continue;
				}
				Country[] countries = arch.GetColumn<Country>();
				_state.SelectedCountry.Set(true, countries[0].CountryId);
				return;
			}
			_state.SelectedCountry.Set(false, "");
		}

		void UpdateTime(IReadOnlyWorld world, int gameTimeEntity) {
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
			_state.Time.Set(time.CurrentTime, time.IsPaused, time.MultiplierIndex);
		}

		void UpdateLocale(IReadOnlyWorld world, int localeEntity) {
			ref Locale locale = ref world.Get<Locale>(localeEntity);
			_state.Locale.Set(locale.Value);
		}
	}
}
