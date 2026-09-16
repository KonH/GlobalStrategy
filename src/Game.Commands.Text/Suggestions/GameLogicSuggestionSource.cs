using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;

namespace GS.Game.Commands.Text.Suggestions {
	public sealed class GameLogicSuggestionSource : ISuggestionSource {
		readonly GameLogic _logic;

		public GameLogicSuggestionSource(GameLogic logic) {
			_logic = logic ?? throw new ArgumentNullException(nameof(logic));
		}

		public IReadOnlyList<string> CountryIds => Union(_logic.CountryConfig.Countries.Select(c => c.CountryId), LiveIds<Country>(c => c.CountryId));
		public IReadOnlyList<string> OrgIds => Union(_logic.OrganizationConfig.Organizations.Select(o => o.OrganizationId), LiveIds<Organization>(o => o.OrganizationId));
		public IReadOnlyList<string> ProvinceIds => Union(_logic.ProvinceConfig.Provinces.Select(p => p.ProvinceId), LiveIds<ProvinceOwnership>(p => p.ProvinceId));
		public IReadOnlyList<string> ActionIds => Union(_logic.ActionConfig.Actions.Select(a => a.ActionId), LiveIds<GameAction>(a => a.ActionId));
		public IReadOnlyList<string> RoleIds => _logic.CharacterConfig.Roles.Select(r => r.RoleId).Where(id => !string.IsNullOrEmpty(id)).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
		public IReadOnlyList<string> LocaleIds { get; } = new[] { "en", "ru" };

		public IReadOnlyList<string> ResourceIds {
			get {
				var ids = new List<string>();
				ids.Add(ResourceDefinitions.Gold);
				ids.Add(ResourceDefinitions.Population);
				ids.Add(ResourceDefinitions.CountryPopulation);
				ids.Add(ResourceDefinitions.CountryScore);
				ids.Add(ResourceDefinitions.OrgScore);
				ids.Add(ResourceDefinitions.Recruits);
				ids.Add(ResourceDefinitions.TroopsDamageBonusPercent);
				ids.Add(ResourceDefinitions.Damage);
				ids.Add(ResourceDefinitions.Durability);
				ids.Add(ResourceDefinitions.WarInitiative);
				ids.Add(ResourceDefinitions.WarProgress);
				foreach (var def in _logic.ResourceConfig.Resources) {
					ids.Add(def.ResourceId);
				}
				foreach (var skill in _logic.CharacterConfig.Skills) {
					ids.Add(skill.SkillId);
				}
				ids.AddRange(LiveIds<Resource>(r => r.ResourceId));
				return DistinctSorted(ids);
			}
		}

		public IReadOnlyList<string> CharacterIds => Union(
			AllConfigCharacterIds(),
			LiveIds<Character>(c => c.CharacterId));

		public IReadOnlyList<string> WarIds => Union(Array.Empty<string>(), LiveIds<War>(w => w.WarId));
		public IReadOnlyList<string> BattleIds => Union(Array.Empty<string>(), LiveIds<Battle>(b => b.BattleId));

		public IReadOnlyList<string> EffectIds {
			get {
				var ids = new List<string>();
				foreach (var def in _logic.ResourceConfig.Resources) {
					foreach (var effect in def.DefaultEffects) {
						ids.Add(effect.EffectId);
					}
				}
				ids.AddRange(LiveIds<ControlEffect>(e => e.EffectId));
				ids.AddRange(LiveIds<ResourceEffect>(e => e.EffectId));
				ids.AddRange(LiveIds<ResourceChange>(e => e.EffectId));
				ids.AddRange(LiveIds<SetCountryRelationEffect>(e => e.EffectId));
				ids.AddRange(LiveIds<ClearCountryRelationEffect>(e => e.EffectId));
				return DistinctSorted(ids);
			}
		}

		public IReadOnlyList<string> TaskIds => Union(_logic.TasksConfig.Tasks.Select(t => t.TaskId), LiveIds<TaskId>(t => t.Value));
		public IReadOnlyList<string> CollectorIds => Union(_logic.CollectorIds, LiveIds<ResourceCollector>(c => c.CollectorId));
		public IReadOnlyList<string> CountryOrOrgIds => DistinctSorted(CountryIds.Concat(OrgIds).ToList());
		public IReadOnlyList<string> OwnerUnionIds => DistinctSorted(CountryOrOrgIds.Concat(CharacterIds).Concat(ProvinceIds).Concat(WarIds).ToList());

		public string CountryDisplayName(string id) {
			return _logic.CountryConfig.FindByCountryId(id)?.DisplayName ?? "";
		}

		public string OrgDisplayName(string id) {
			return _logic.OrganizationConfig.FindById(id)?.DisplayName ?? "";
		}

		public string ProvinceDisplayName(string id) {
			return id;
		}

		public string ActionNameKey(string id) {
			return _logic.ActionConfig.Find(id)?.NameKey ?? "";
		}

		public string RoleNameKey(string id) {
			return _logic.CharacterConfig.FindRole(id)?.NameKey ?? "";
		}

		IEnumerable<string> AllConfigCharacterIds() {
			foreach (var pool in _logic.CharacterConfig.CountryPools) {
				foreach (var slot in pool.Slots.Values) {
					foreach (var entry in slot) {
						yield return entry.CharacterId;
					}
				}
			}
			foreach (var pool in _logic.CharacterConfig.OrgPools) {
				foreach (var slot in pool.Slots.Values) {
					foreach (var entry in slot) {
						yield return entry.CharacterId;
					}
				}
			}
		}

		List<string> LiveIds<T>(Func<T, string> selector) where T : struct {
			var ids = new List<string>();
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in _logic.World.GetMatchingArchetypes(required, null)) {
				T[] column = arch.GetColumn<T>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string id = selector(column[i]);
					if (!string.IsNullOrEmpty(id)) {
						ids.Add(id);
					}
				}
			}
			return ids;
		}

		static IReadOnlyList<string> Union(IEnumerable<string> catalog, IEnumerable<string> live) {
			return DistinctSorted(catalog.Concat(live).ToList());
		}

		static List<string> DistinctSorted(IReadOnlyList<string> ids) {
			var set = new HashSet<string>(StringComparer.Ordinal);
			foreach (string id in ids) {
				if (!string.IsNullOrEmpty(id)) {
					set.Add(id);
				}
			}
			var list = set.ToList();
			list.Sort(StringComparer.Ordinal);
			return list;
		}
	}
}
