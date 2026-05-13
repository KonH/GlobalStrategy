using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Main {
	static class InitSystem {
		public static bool Update(World world, GameLogicContext context, Random rng) {
			int[] required = { TypeId<IsInitialized>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return false;
				}
			}
			Run(world, context, rng);
			return true;
		}

		static void Run(World world, GameLogicContext context, Random rng) {
			var countryConfig = context.Country.Load();
			var resourceConfig = context.Resource.Load();

			foreach (var entry in countryConfig.Countries) {
				if (!entry.IsAvailable) {
					continue;
				}
				int entity = world.Create();
				world.Add(entity, new Country(entry.CountryId));
				if (entry.CountryId == context.InitialPlayerCountryId) {
					world.Add(entity, new Player());
				}
				CreateResourceEntities(world, entry, resourceConfig);
			}

			var settings = context.GameSettings.Load();
			var startTime = new DateTime(settings.StartYear, 1, 1);

			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime {
				CurrentTime = startTime,
				IsPaused = false,
				MultiplierIndex = 0
			});

			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = settings.DefaultLocale });

			int settingsEntity = world.Create();
			world.Add(settingsEntity, new AppSettings {
				Locale = settings.DefaultLocale,
				AutoSaveInterval = ParseAutoSaveInterval(settings.AutoSaveInterval)
			});

			var orgConfig = context.Organization.Load();
			var orgEntry = orgConfig.FindById(context.InitialOrganizationId);
			if (!string.IsNullOrEmpty(context.InitialOrganizationId) && orgEntry == null) {
				context.Logger?.LogError(
					$"[InitSystem] Organization '{context.InitialOrganizationId}' not found in config.");
			}
			if (orgEntry != null) {
				int orgEntity = world.Create();
				world.Add(orgEntity, new Organization {
					OrganizationId = orgEntry.OrganizationId,
					DisplayName = orgEntry.DisplayName
				});

				int orgGoldEntity = world.Create();
				world.Add(orgGoldEntity, new ResourceOwner(orgEntry.OrganizationId));
				world.Add(orgGoldEntity, new Resource { ResourceId = "gold", Value = orgEntry.InitialGold });

				int influenceEntity = world.Create();
				world.Add(influenceEntity, new InfluenceEffect {
					OrgId     = orgEntry.OrganizationId,
					CountryId = orgEntry.HqCountryId,
					Value     = orgEntry.BaseInfluence,
					EffectId  = $"base_{orgEntry.OrganizationId}"
				});
			}

			CreateCharacterEntities(world, context, rng);

			int initEntity = world.Create();
			world.Add(initEntity, new IsInitialized());
		}

		static void CreateCharacterEntities(World world, GameLogicContext context, Random rng) {
			var characterConfig = context.Character.Load();
			if (characterConfig.Roles.Count == 0) {
				return;
			}
			var countryConfig = context.Country.Load();
			foreach (var entry in countryConfig.Countries) {
				if (!entry.IsAvailable) {
					continue;
				}
				var pool = characterConfig.FindPool(entry.CountryId);
				if (pool == null) {
					continue;
				}
				foreach (var role in characterConfig.Roles) {
					if (!pool.Slots.TryGetValue(role.RoleId, out var slotList) || slotList.Count == 0) {
						continue;
					}
					var charEntry = slotList[rng.Next(slotList.Count)];
					int charEntity = world.Create();
					var namePartKeys = new string[charEntry.NamePartKeys.Count];
					for (int i = 0; i < charEntry.NamePartKeys.Count; i++) {
						namePartKeys[i] = charEntry.NamePartKeys[i];
					}
					world.Add(charEntity, new Character {
						CharacterId = charEntry.CharacterId,
						CountryId = entry.CountryId,
						RoleId = role.RoleId,
						NamePartKeys = namePartKeys
					});
					foreach (var skillDef in characterConfig.Skills) {
						int skillValue;
						if (charEntry.Skills.TryGetValue(skillDef.SkillId, out var skillSettings)) {
							skillValue = rng.Next(skillSettings.MinValue, skillSettings.MaxValue + 1);
						} else {
							skillValue = rng.Next(5, 31);
						}
						int skillEntity = world.Create();
						world.Add(skillEntity, new ResourceOwner(charEntry.CharacterId));
						world.Add(skillEntity, new Resource { ResourceId = skillDef.SkillId, Value = skillValue });
					}
				}
			}
		}

		static void CreateResourceEntities(World world, CountryEntry entry, ResourceConfig resourceConfig) {
			foreach (var resourceDef in resourceConfig.Resources) {
				double initialValue = resourceDef.DefaultInitialValue;
				foreach (var init in entry.InitialResources) {
					if (init.ResourceId == resourceDef.ResourceId) {
						initialValue = init.Value;
						break;
					}
				}

				int resourceEntity = world.Create();
				world.Add(resourceEntity, new ResourceOwner(entry.CountryId));
				world.Add(resourceEntity, new Resource { ResourceId = resourceDef.ResourceId, Value = initialValue });

				foreach (var effectDef in resourceDef.DefaultEffects) {
					int effectEntity = world.Create();
					world.Add(effectEntity, new ResourceOwner(entry.CountryId));
					world.Add(effectEntity, new ResourceLink(resourceDef.ResourceId));
					world.Add(effectEntity, new ResourceEffect {
						EffectId = effectDef.EffectId,
						Value = effectDef.Value,
						PayType = Enum.Parse<PayType>(effectDef.PayType, ignoreCase: true)
					});
				}
			}
		}

		static AutoSaveInterval ParseAutoSaveInterval(string value) {
			return value.ToLowerInvariant() switch {
				"daily"  => AutoSaveInterval.Daily,
				"yearly" => AutoSaveInterval.Yearly,
				_        => AutoSaveInterval.Monthly
			};
		}
	}
}
