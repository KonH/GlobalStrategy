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

			BuildProximityMap(world, context);
			CreateActionEntities(world, context, rng);
			CreateOrgCharacterEntities(world, context, rng);
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
						OrgId = "",
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

		static void CreateOrgCharacterEntities(World world, GameLogicContext context, Random rng) {
			var characterConfig = context.Character.Load();
			var orgConfig = context.Organization.Load();

			string orgId = context.InitialOrganizationId;
			if (string.IsNullOrEmpty(orgId)) { return; }
			var orgEntry = orgConfig.FindById(orgId);
			if (orgEntry == null) { return; }

			bool isPlayerOrg = true;
			var pool = characterConfig.FindOrgPool(orgId);

			CreateOrgSlots(world, characterConfig, rng, orgId, "master", 1, pool, isPlayerOrg);

			int agentSlots = orgEntry.InitialAgentSlots;
			if (agentSlots > 0) {
				CreateOrgSlots(world, characterConfig, rng, orgId, "agent", agentSlots, pool, isPlayerOrg);
			}
		}

		static void CreateOrgSlots(
			World world, CharacterConfig characterConfig, Random rng,
			string orgId, string roleId, int totalSlots,
			OrgCharacterPool? pool, bool isPlayerOrg) {

			List<CharacterEntry>? candidates = null;
			if (pool != null) {
				pool.Slots.TryGetValue(roleId, out candidates);
			}

			for (int slotIndex = 0; slotIndex < totalSlots; slotIndex++) {
				bool filled = slotIndex == 0 && candidates != null && candidates.Count > 0;
				string charId = "";

				if (filled) {
					var charEntry = candidates![rng.Next(candidates.Count)];
					charId = charEntry.CharacterId;

					int charEntity = world.Create();
					var namePartKeys = new string[charEntry.NamePartKeys.Count];
					for (int i = 0; i < charEntry.NamePartKeys.Count; i++) {
						namePartKeys[i] = charEntry.NamePartKeys[i];
					}
					world.Add(charEntity, new Character {
						CharacterId = charId,
						CountryId = "",
						OrgId = orgId,
						RoleId = roleId,
						NamePartKeys = namePartKeys
					});

					var roleDef = characterConfig.FindRole(roleId);
					var roleSkillIds = roleDef != null
						? new System.Collections.Generic.HashSet<string>(roleDef.SkillIds)
						: new System.Collections.Generic.HashSet<string>();

					foreach (var skillDef in characterConfig.Skills) {
						if (!roleSkillIds.Contains(skillDef.SkillId)) {
							continue;
						}
						int skillValue;
						if (charEntry.Skills.TryGetValue(skillDef.SkillId, out var ss)) {
							skillValue = rng.Next(ss.MinValue, ss.MaxValue + 1);
						} else {
							skillValue = rng.Next(5, 31);
						}
						int skillEntity = world.Create();
						world.Add(skillEntity, new ResourceOwner(charId));
						world.Add(skillEntity, new Resource { ResourceId = skillDef.SkillId, Value = skillValue });
					}
				}

				int slotEntity = world.Create();
				world.Add(slotEntity, new CharacterSlot {
					OwnerId = orgId,
					RoleId = roleId,
					SlotIndex = slotIndex,
					IsAvailable = !filled && isPlayerOrg,
					CharacterId = charId
				});
			}
		}

		static AutoSaveInterval ParseAutoSaveInterval(string value) {
			return value.ToLowerInvariant() switch {
				"daily"  => AutoSaveInterval.Daily,
				"yearly" => AutoSaveInterval.Yearly,
				_        => AutoSaveInterval.Monthly
			};
		}

		internal static void BuildProximityMap(World world, GameLogicContext context) {
			int[] pmReq = { TypeId<ProximityMapData>.Value };
			var toDestroy = new System.Collections.Generic.List<int>();
			foreach (var arch in world.GetMatchingArchetypes(pmReq, null)) {
				for (int i = 0; i < arch.Count; i++) {
					toDestroy.Add(arch.Entities[i]);
				}
			}
			foreach (int e in toDestroy) { world.Destroy(e); }

			var countryConfig = context.Country.Load();
			var featureGeometry = context.MapGeometry?.Load();
			var distances = new System.Collections.Generic.Dictionary<(string, string), float>();

			if (featureGeometry != null) {
				var featurePoints = BuildFeaturePointsLookup(featureGeometry);

				var entries = new System.Collections.Generic.List<CountryEntry>();
				foreach (var e in countryConfig.Countries) {
					if (e.IsAvailable) { entries.Add(e); }
				}

				for (int i = 0; i < entries.Count; i++) {
					for (int j = i + 1; j < entries.Count; j++) {
						float dist = ComputeMinDistance(entries[i], entries[j], featurePoints);
						string a = entries[i].CountryId;
						string b = entries[j].CountryId;
						if (string.CompareOrdinal(a, b) > 0) { var tmp = a; a = b; b = tmp; }
						distances[(a, b)] = dist;
					}
				}
			}

			int pmEntity = world.Create();
			world.Add(pmEntity, new ProximityMapData { Distances = distances });
		}

		static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GS.Core.Map.Vector2d>>
			BuildFeaturePointsLookup(System.Collections.Generic.List<GS.Core.Map.MapFeature> features) {
			var lookup = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GS.Core.Map.Vector2d>>();
			foreach (var f in features) {
				var pts = new System.Collections.Generic.List<GS.Core.Map.Vector2d>();
				foreach (var poly in f.Polygons) {
					if (poly.Rings.Count == 0) { continue; }
					var ring = poly.Rings[0];
					for (int k = 0; k < ring.Points.Count; k += 4) {
						pts.Add(ring.Points[k]);
					}
				}
				lookup[f.Id] = pts;
			}
			return lookup;
		}

		static float ComputeMinDistance(
			CountryEntry a, CountryEntry b,
			System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<GS.Core.Map.Vector2d>> featurePoints) {
			float minDist = float.MaxValue;
			var aIds = new System.Collections.Generic.List<string>(a.MainMapFeatureIds);
			foreach (var s in a.SecondaryMapFeatureIds) { aIds.Add(s); }
			var bIds = new System.Collections.Generic.List<string>(b.MainMapFeatureIds);
			foreach (var s in b.SecondaryMapFeatureIds) { bIds.Add(s); }

			foreach (var aId in aIds) {
				if (!featurePoints.TryGetValue(aId, out var aPts)) { continue; }
				foreach (var bId in bIds) {
					if (!featurePoints.TryGetValue(bId, out var bPts)) { continue; }
					foreach (var ap in aPts) {
						foreach (var bp in bPts) {
							float dx = (float)(ap.Lon - bp.Lon);
							float dy = (float)(ap.Lat - bp.Lat);
							float d = dx * dx + dy * dy;
							if (d < minDist) { minDist = d; }
						}
					}
				}
			}
			return minDist == float.MaxValue ? 1e9f : (float)System.Math.Sqrt(minDist);
		}

		static void CreateActionEntities(World world, GameLogicContext context, Random rng) {
			var actionConfig = context.Action.Load();
			string orgId = context.InitialOrganizationId;
			if (string.IsNullOrEmpty(orgId)) { return; }

			int handSize = actionConfig.GetHandSize("org");
			if (handSize <= 0) { return; }

			var pool = actionConfig.GetOrgPool(orgId);
			if (pool == null || pool.Count == 0) { return; }

			int ownerEntity = world.Create();
			world.Add(ownerEntity, new ActionOwner {
				OwnerId   = orgId,
				OwnerType = "org",
				HandSize  = handSize
			});

			for (int i = 0; i < pool.Count; i++) {
				int cardEntity = world.Create();
				world.Add(cardEntity, new ActionCard {
					ActionId = pool[i],
					OwnerId  = orgId
				});
			}

			var deckEntities = new System.Collections.Generic.List<int>();
			int[] cardReq = { TypeId<ActionCard>.Value };
			foreach (var arch in world.GetMatchingArchetypes(cardReq, null)) {
				ActionCard[] cards = arch.GetColumn<ActionCard>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (cards[i].OwnerId == orgId) { deckEntities.Add(arch.Entities[i]); }
				}
			}
			for (int i = deckEntities.Count - 1; i > 0; i--) {
				int j = rng.Next(i + 1);
				var tmp = deckEntities[i]; deckEntities[i] = deckEntities[j]; deckEntities[j] = tmp;
			}
			for (int slot = 0; slot < handSize && slot < deckEntities.Count; slot++) {
				world.Add(deckEntities[slot], new InHand { SlotIndex = slot });
			}

			DiscoverInitialCountries(world, context);
		}

		static void DiscoverInitialCountries(World world, GameLogicContext context) {
			var toDiscover = new System.Collections.Generic.HashSet<string>();

			if (!string.IsNullOrEmpty(context.InitialPlayerCountryId)) {
				toDiscover.Add(context.InitialPlayerCountryId);
			}

			if (!string.IsNullOrEmpty(context.InitialOrganizationId)) {
				var orgConfig = context.Organization.Load();
				var orgEntry = orgConfig.FindById(context.InitialOrganizationId);
				if (orgEntry != null && !string.IsNullOrEmpty(orgEntry.HqCountryId)) {
					toDiscover.Add(orgEntry.HqCountryId);
				}
			}

			// Collect entity IDs first — calling world.Add inside GetMatchingArchetypes would
			// create new archetypes and mutate the dictionary mid-iteration, throwing InvalidOperationException.
			var entitiesToDiscover = new System.Collections.Generic.List<int>();
			int[] countryReq = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (toDiscover.Contains(countries[i].CountryId)) {
						entitiesToDiscover.Add(arch.Entities[i]);
					}
				}
			}
			foreach (int entity in entitiesToDiscover) {
				world.Add(entity, new IsDiscovered());
			}
		}
	}
}
