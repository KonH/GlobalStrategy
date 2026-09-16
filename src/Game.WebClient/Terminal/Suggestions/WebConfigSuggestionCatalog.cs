using System.Collections.Generic;
using System.Linq;
using GS.Game.Commands.Text.Suggestions;
using GS.Game.Configs;
using GS.Game.WebClient.Services;

namespace GS.Game.WebClient.Terminal.Suggestions {
	public static class WebConfigSuggestionCatalog {
		public static ISuggestionSource From(IGameConfigSource config) {
			CountryConfig countries = config.Country.Load();
			OrganizationConfig orgs = config.Organization.Load();
			ProvinceConfig provinces = config.Province.Load();
			ActionConfig actions = config.Action.Load();
			CharacterConfig characters = config.Character.Load();
			ResourceConfig resources = config.Resource.Load();
			TasksConfig tasks = config.Tasks.Load();

			var countryNames = countries.Countries.ToDictionary(c => c.CountryId, c => c.DisplayName);
			var orgNames = orgs.Organizations.ToDictionary(o => o.OrganizationId, o => o.DisplayName);
			var provinceNames = provinces.Provinces.ToDictionary(p => p.ProvinceId, p => p.ProvinceId);
			var actionKeys = actions.Actions.ToDictionary(a => a.ActionId, a => a.NameKey);
			var roleKeys = characters.Roles.ToDictionary(r => r.RoleId, r => r.NameKey);

			var resourceIds = new List<string> {
				ResourceDefinitions.Gold,
				ResourceDefinitions.Population,
				ResourceDefinitions.CountryPopulation,
				ResourceDefinitions.CountryScore,
				ResourceDefinitions.OrgScore,
				ResourceDefinitions.Recruits,
				ResourceDefinitions.TroopsDamageBonusPercent,
				ResourceDefinitions.Damage,
				ResourceDefinitions.Durability,
				ResourceDefinitions.WarInitiative,
				ResourceDefinitions.WarProgress
			};
			foreach (var def in resources.Resources) {
				resourceIds.Add(def.ResourceId);
			}
			foreach (var skill in characters.Skills) {
				resourceIds.Add(skill.SkillId);
			}

			var characterIds = new List<string>();
			foreach (var pool in characters.CountryPools) {
				foreach (var slot in pool.Slots.Values) {
					foreach (var entry in slot) {
						characterIds.Add(entry.CharacterId);
					}
				}
			}
			foreach (var pool in characters.OrgPools) {
				foreach (var slot in pool.Slots.Values) {
					foreach (var entry in slot) {
						characterIds.Add(entry.CharacterId);
					}
				}
			}

			var effectIds = new List<string>();
			foreach (var def in resources.Resources) {
				foreach (var effect in def.DefaultEffects) {
					effectIds.Add(effect.EffectId);
				}
			}

			return new ConfigCatalogSuggestionSource(
				countries.Countries.Select(c => c.CountryId).ToList(),
				orgs.Organizations.Select(o => o.OrganizationId).ToList(),
				provinces.Provinces.Select(p => p.ProvinceId).ToList(),
				actions.Actions.Select(a => a.ActionId).ToList(),
				characters.Roles.Select(r => r.RoleId).ToList(),
				resourceIds,
				characterIds,
				System.Array.Empty<string>(),
				System.Array.Empty<string>(),
				effectIds,
				tasks.Tasks.Select(t => t.TaskId).ToList(),
				System.Array.Empty<string>(),
				countryDisplayNames: countryNames,
				orgDisplayNames: orgNames,
				provinceDisplayNames: provinceNames,
				actionNameKeys: actionKeys,
				roleNameKeys: roleKeys);
		}
	}
}
