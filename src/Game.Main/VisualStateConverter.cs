using System.Collections.Generic;
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
			UpdatePlayerCountry(world);
			UpdateTime(world, gameTimeEntity);
			UpdateLocale(world, localeEntity);
			UpdateResources(world);
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

		void UpdatePlayerCountry(IReadOnlyWorld world) {
			int[] required = { TypeId<Country>.Value, TypeId<Player>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count == 0) {
					continue;
				}
				Country[] countries = arch.GetColumn<Country>();
				_state.PlayerCountry.Set(true, countries[0].CountryId);
				return;
			}
			_state.PlayerCountry.Set(false, "");
		}

		void UpdateTime(IReadOnlyWorld world, int gameTimeEntity) {
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
			_state.Time.Set(time.CurrentTime, time.IsPaused, time.MultiplierIndex);
		}

		void UpdateLocale(IReadOnlyWorld world, int localeEntity) {
			ref Locale locale = ref world.Get<Locale>(localeEntity);
			_state.Locale.Set(locale.Value);
		}

		void UpdateResources(IReadOnlyWorld world) {
			string playerCountryId = _state.PlayerCountry.IsValid ? _state.PlayerCountry.CountryId : "";
			string selectedCountryId = _state.SelectedCountry.IsValid ? _state.SelectedCountry.CountryId : "";

			_state.PlayerResources.Set(
				_state.PlayerCountry.IsValid,
				playerCountryId,
				BuildResources(world, playerCountryId));
			_state.SelectedResources.Set(
				_state.SelectedCountry.IsValid,
				selectedCountryId,
				BuildResources(world, selectedCountryId));
		}

		List<ResourceStateEntry> BuildResources(IReadOnlyWorld world, string countryId) {
			var result = new List<ResourceStateEntry>();
			if (string.IsNullOrEmpty(countryId)) {
				return result;
			}
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].CountryId != countryId) {
						continue;
					}
					var effects = BuildEffects(world, countryId, resources[i].ResourceId);
					result.Add(new ResourceStateEntry(resources[i].ResourceId, resources[i].Value, effects));
				}
			}
			return result;
		}

		List<EffectStateEntry> BuildEffects(IReadOnlyWorld world, string countryId, string resourceId) {
			var result = new List<EffectStateEntry>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				ResourceLink[] links = arch.GetColumn<ResourceLink>();
				ResourceEffect[] effects = arch.GetColumn<ResourceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (owners[i].CountryId != countryId || links[i].ResourceId != resourceId) {
						continue;
					}
					result.Add(new EffectStateEntry(effects[i].EffectId, effects[i].Value, effects[i].PayType));
				}
			}
			return result;
		}
	}
}
