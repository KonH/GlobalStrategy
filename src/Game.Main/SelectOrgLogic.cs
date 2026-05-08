using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	public class SelectOrgLogic {
		readonly World _world = new World();
		readonly CommandAccessor _commandAccessor = new CommandAccessor();
		readonly Dictionary<string, OrganizationEntry> _hqToOrg = new Dictionary<string, OrganizationEntry>();

		public VisualState VisualState { get; } = new VisualState();
		public IWriteOnlyCommandAccessor Commands { get; }
		public IReadOnlyList<string> HqCountryIds { get; }

		public SelectOrgLogic(
			IConfigSource<GS.Game.Configs.CountryConfig> countryConfig,
			IConfigSource<OrganizationConfig> orgConfig) {
			Commands = (IWriteOnlyCommandAccessor)_commandAccessor;

			var config = countryConfig.Load();
			foreach (var entry in config.Countries) {
				int entity = _world.Create();
				_world.Add(entity, new Country(entry.CountryId));
			}

			var orgs = orgConfig.Load();
			var hqIds = new List<string>();
			foreach (var org in orgs.Organizations) {
				_hqToOrg[org.HqCountryId] = org;
				hqIds.Add(org.HqCountryId);
			}
			HqCountryIds = hqIds;
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
				string countryId = arch.GetColumn<Country>()[0].CountryId;
				VisualState.SelectedCountry.Set(true, countryId);

				if (_hqToOrg.TryGetValue(countryId, out var org)) {
					VisualState.SelectedOrganization.Set(true, org.OrganizationId, org.DisplayName, org.InitialGold);
				} else {
					VisualState.SelectedOrganization.Set(false, "", "", 0);
				}
				return;
			}
			VisualState.SelectedCountry.Set(false, "");
			VisualState.SelectedOrganization.Set(false, "", "", 0);
		}
	}
}
