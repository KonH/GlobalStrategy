using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using GS.Game.Configs;

namespace GS.Main {
	class VisualStateConverter {
		readonly VisualState _state;

		static readonly string[] s_roleOrder = { "ruler", "military_advisor", "diplomacy_advisor", "economic_advisor", "secret_advisor" };

		internal VisualStateConverter(VisualState state) {
			_state = state;
		}

		internal void Update(IReadOnlyWorld world, int gameTimeEntity, int localeEntity, int orgEntity) {
			UpdateSelectedCountry(world);
			UpdatePlayerCountry(world);
			UpdateTime(world, gameTimeEntity);
			UpdateLocale(world, localeEntity);
			UpdatePlayerOrganization(world, orgEntity);
			UpdateResources(world);
			UpdateSelectedInfluence(world);
			UpdateCharacters(world);
		}

		void UpdateCharacters(IReadOnlyWorld world) {
			if (!_state.SelectedCountry.IsValid) {
				_state.SelectedCharacters.Set(new List<CharacterStateEntry>());
				return;
			}
			string selectedCountryId = _state.SelectedCountry.CountryId;

			var charData = new Dictionary<string, (string roleId, string[] namePartKeys)>();
			var charSkills = new Dictionary<string, List<SkillEntry>>();

			int[] charRequired = { TypeId<Character>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(charRequired, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (chars[i].CountryId != selectedCountryId) {
						continue;
					}
					charData[chars[i].CharacterId] = (chars[i].RoleId, chars[i].NamePartKeys);
					charSkills[chars[i].CharacterId] = new List<SkillEntry>();
				}
			}

			int[] resRequired = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(resRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (charSkills.TryGetValue(owners[i].OwnerId, out var skillList)) {
						skillList.Add(new SkillEntry(resources[i].ResourceId, (int)resources[i].Value));
					}
				}
			}

			var entries = new List<CharacterStateEntry>();
			foreach (var (charId, (roleId, namePartKeys)) in charData) {
				entries.Add(new CharacterStateEntry(charId, roleId, namePartKeys, charSkills[charId]));
			}
			entries.Sort((a, b) => {
				int ai = Array.IndexOf(s_roleOrder, a.RoleId);
				int bi = Array.IndexOf(s_roleOrder, b.RoleId);
				if (ai < 0) { ai = int.MaxValue; }
				if (bi < 0) { bi = int.MaxValue; }
				return ai.CompareTo(bi);
			});

			_state.SelectedCharacters.Set(entries);
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

		void UpdatePlayerOrganization(IReadOnlyWorld world, int orgEntity) {
			if (orgEntity < 0) {
				_state.PlayerOrganization.Set(false, "", "");
				return;
			}
			ref Organization org = ref world.Get<Organization>(orgEntity);
			_state.PlayerOrganization.Set(true, org.OrganizationId, org.DisplayName);
		}

		void UpdateResources(IReadOnlyWorld world) {
			string playerOrgId = _state.PlayerOrganization.IsValid ? _state.PlayerOrganization.OrgId : "";
			string selectedCountryId = _state.SelectedCountry.IsValid ? _state.SelectedCountry.CountryId : "";

			List<InfluenceIncomeEntry>? orgInfluenceIncomes = null;
			if (_state.PlayerOrganization.IsValid) {
				orgInfluenceIncomes = BuildInfluenceIncomesForOrg(world, playerOrgId);
			}

			_state.PlayerResources.Set(
				_state.PlayerOrganization.IsValid,
				playerOrgId,
				BuildResources(world, playerOrgId),
				orgInfluenceIncomes);
			_state.SelectedResources.Set(
				_state.SelectedCountry.IsValid,
				selectedCountryId,
				BuildResources(world, selectedCountryId));
		}

		List<InfluenceIncomeEntry> BuildInfluenceIncomesForOrg(IReadOnlyWorld world, string orgId) {
			var result = new List<InfluenceIncomeEntry>();
			int[] required = { TypeId<InfluenceEffect>.Value };
			var byCountry = new Dictionary<string, int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId != orgId) {
						continue;
					}
					string cid = effects[i].CountryId;
					if (!byCountry.TryGetValue(cid, out int v)) {
						v = 0;
					}
					byCountry[cid] = v + effects[i].Value;
				}
			}
			foreach (var (countryId, influence) in byCountry) {
				double baseIncome = InfluenceSystem.ComputeBaseMonthlyGold(world, countryId);
				double gain = Math.Round((influence / 100.0) * baseIncome, 2);
				result.Add(new InfluenceIncomeEntry(countryId, gain));
			}
			return result;
		}

		void UpdateSelectedInfluence(IReadOnlyWorld world) {
			string selectedCountryId = _state.SelectedCountry.IsValid ? _state.SelectedCountry.CountryId : "";
			if (!_state.SelectedCountry.IsValid) {
				_state.SelectedInfluence.Set(0, new List<OrgInfluenceEntry>());
				return;
			}

			// Gather org display names
			var orgNames = new Dictionary<string, string>();
			int[] orgRequired = { TypeId<Organization>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(orgRequired, null)) {
				Organization[] orgs = arch.GetColumn<Organization>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					orgNames[orgs[i].OrganizationId] = orgs[i].DisplayName;
				}
			}

			// Group influence by org for the selected country (track base vs permanent)
			var byOrgBase = new Dictionary<string, int>();
			var byOrgPermanent = new Dictionary<string, int>();
			int[] influenceRequired = { TypeId<InfluenceEffect>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(influenceRequired, null)) {
				InfluenceEffect[] effects = arch.GetColumn<InfluenceEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].CountryId != selectedCountryId) {
						continue;
					}
					string oid = effects[i].OrgId;
					bool isBase = effects[i].EffectId.StartsWith("base_");
					if (isBase) {
						if (!byOrgBase.TryGetValue(oid, out int v)) {
							v = 0;
						}
						byOrgBase[oid] = v + effects[i].Value;
					} else {
						if (!byOrgPermanent.TryGetValue(oid, out int v)) {
							v = 0;
						}
						byOrgPermanent[oid] = v + effects[i].Value;
					}
				}
			}

			double baseIncome = InfluenceSystem.ComputeBaseMonthlyGold(world, selectedCountryId);
			int usedTotal = 0;
			var entries = new List<OrgInfluenceEntry>();
			var allOrgs = new System.Collections.Generic.HashSet<string>(byOrgBase.Keys);
			foreach (var k in byOrgPermanent.Keys) {
				allOrgs.Add(k);
			}
			foreach (string orgId in allOrgs) {
				int baseInfl = byOrgBase.TryGetValue(orgId, out int b) ? b : 0;
				int permInfl = byOrgPermanent.TryGetValue(orgId, out int p) ? p : 0;
				int influence = baseInfl + permInfl;
				usedTotal += influence;
				string displayName = orgNames.TryGetValue(orgId, out string? n) ? n : orgId;
				double gain = Math.Round((influence / 100.0) * baseIncome, 2);
				entries.Add(new OrgInfluenceEntry(orgId, displayName, influence, baseInfl, permInfl, gain));
			}
			entries.Sort((a, b) => b.Influence.CompareTo(a.Influence));
			_state.SelectedInfluence.Set(usedTotal, entries);
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
					if (owners[i].OwnerId != countryId) {
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
					if (owners[i].OwnerId != countryId || links[i].ResourceId != resourceId) {
						continue;
					}
					result.Add(new EffectStateEntry(effects[i].EffectId, effects[i].Value, effects[i].PayType));
				}
			}
			return result;
		}
	}
}
