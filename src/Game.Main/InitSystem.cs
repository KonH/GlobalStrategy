using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

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
				CreateResourceEntities(world, entry, resourceConfig);
			}

			CreateCountryPopulationEntities(world, countryConfig);

			var provinceConfig = context.Province.Load();
			ProvinceOwnershipSystem.Seed(world, provinceConfig);
			CreateProvincePopulationEntities(world, provinceConfig);

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

			int botActionLogEntity = world.Create();
			world.Add(botActionLogEntity, new BotActionLog { Entries = Array.Empty<string>() });

			var orgConfig = context.Organization.Load();
			var participating = ResolveParticipatingOrgs(context, orgConfig);

			foreach (var orgEntry in participating) {
				int orgEntity = world.Create();
				world.Add(orgEntity, new Organization {
					OrganizationId = orgEntry.OrganizationId,
					DisplayName = orgEntry.DisplayName
				});
				if (orgEntry.OrganizationId != context.InitialOrganizationId) {
					world.Add(orgEntity, new BotControlled());
				} else {
					world.Add(orgEntity, new Player());
				}

				int orgGoldEntity = world.Create();
				world.Add(orgGoldEntity, new ResourceOwner(orgEntry.OrganizationId));
				world.Add(orgGoldEntity, new Resource { ResourceId = "gold", Value = orgEntry.InitialGold });

				int controlEntity = world.Create();
				world.Add(controlEntity, new ControlEffect {
					OrgId     = orgEntry.OrganizationId,
					CountryId = orgEntry.HqCountryId,
					Value     = orgEntry.BaseControl,
					EffectId  = $"base_{orgEntry.OrganizationId}"
				});
			}

			BuildProximityMap(world, context);
			CreateActionEntities(world, context, rng, participating);
			CreateOrgCharacterEntities(world, context, rng, participating);
			CreateCharacterEntities(world, context, rng);
			CreateCountryActionEntities(world, context, rng, participating);
			DiscoverInitialCountries(world, participating);

			// Bootstrap pass: previousTime == currentTime so no Monthly effect fires (province
			// population growth stays untouched at its seed value), but the Instant
			// country_population/country_score effects created above always fire regardless of
			// the month-boundary gate — giving both resources a correct non-zero value before
			// OrgScoreSystem.Recompute reads country_score below. See
			// Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md.
			var resourceCollectorRegistry = ResourceCollectorRegistry.CreateDefault(
				settings.PopulationGrowthPercentPerMonth, settings.CountryScoreCoefficient);
			ResourceSystem.Update(world, startTime, startTime, resourceCollectorRegistry, settings.ResourceIdUpdateOrder);

			OrgScoreSystem.Recompute(world);

			int initEntity = world.Create();
			world.Add(initEntity, new IsInitialized());
		}

		static List<OrganizationEntry> ResolveParticipatingOrgs(GameLogicContext context, OrganizationConfig orgConfig) {
			var result = new List<OrganizationEntry>();
			if (context.ParticipatingOrganizationIds != null && context.ParticipatingOrganizationIds.Count > 0) {
				foreach (var orgId in context.ParticipatingOrganizationIds) {
					var entry = orgConfig.FindById(orgId);
					if (entry == null) {
						context.Logger?.LogError($"[InitSystem] Organization '{orgId}' not found in organizations config.");
						throw new InvalidOperationException($"Organization '{orgId}' not found in organizations config.");
					}
					result.Add(entry);
				}
				return result;
			}

			if (!string.IsNullOrEmpty(context.InitialOrganizationId)) {
				var entry = orgConfig.FindById(context.InitialOrganizationId);
				if (entry == null) {
					context.Logger?.LogError(
						$"[InitSystem] Organization '{context.InitialOrganizationId}' not found in config.");
				} else {
					result.Add(entry);
				}
			}
			return result;
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
						world.Add(skillEntity, new ResourceOwner(charEntry.CharacterId, OwnerType.Character));
						world.Add(skillEntity, new Resource { ResourceId = skillDef.SkillId, Value = skillValue });
					}
				}
			}
		}

		static void CreateCountryPopulationEntities(World world, CountryConfig config) {
			foreach (var entry in config.Countries) {
				if (!entry.IsAvailable) {
					continue;
				}
				CreateCollectorDrivenCountryResource(world, entry.CountryId, CountryScoreCollector.CountryPopulationResourceId, CountryPopulationCollector.Id);
				CreateCollectorDrivenCountryResource(world, entry.CountryId, CountryScoreCollector.ResourceId, CountryScoreCollector.Id);
			}
		}

		static void CreateCollectorDrivenCountryResource(World world, string countryId, string resourceId, string collectorId) {
			int resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(resourceEntity, new Resource { ResourceId = resourceId, Value = 0 });

			int instantEffectEntity = world.Create();
			world.Add(instantEffectEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(instantEffectEntity, new ResourceLink(resourceId));
			world.Add(instantEffectEntity, new ResourceEffect {
				EffectId = $"{resourceId}_seed_{countryId}",
				PayType = PayType.Instant
			});
			world.Add(instantEffectEntity, new ResourceCollector { CollectorId = collectorId });

			int monthlyEffectEntity = world.Create();
			world.Add(monthlyEffectEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(monthlyEffectEntity, new ResourceLink(resourceId));
			world.Add(monthlyEffectEntity, new ResourceEffect {
				EffectId = $"{resourceId}_monthly_{countryId}",
				PayType = PayType.Monthly
			});
			world.Add(monthlyEffectEntity, new ResourceCollector { CollectorId = collectorId });
		}

		static void CreateProvincePopulationEntities(World world, ProvinceConfig config) {
			foreach (var entry in config.Provinces) {
				int entity = world.Create();
				world.Add(entity, new ResourceOwner(entry.ProvinceId, OwnerType.Province));
				world.Add(entity, new Resource {
					ResourceId = CountryPopulationCollector.ResourceId,
					Value = entry.Population
				});

				int growthEffectEntity = world.Create();
				world.Add(growthEffectEntity, new ResourceOwner(entry.ProvinceId, OwnerType.Province));
				world.Add(growthEffectEntity, new ResourceLink(CountryPopulationCollector.ResourceId));
				world.Add(growthEffectEntity, new ResourceEffect {
					EffectId = $"population_growth_{entry.ProvinceId}",
					PayType = PayType.Monthly
				});
				world.Add(growthEffectEntity, new ResourceCollector { CollectorId = PopulationGrowthCollector.Id });
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

		static void CreateOrgCharacterEntities(World world, GameLogicContext context, Random rng, List<OrganizationEntry> participating) {
			var characterConfig = context.Character.Load();

			foreach (var orgEntry in participating) {
				string orgId = orgEntry.OrganizationId;
				bool isPlayerOrg = true;
				var pool = characterConfig.FindOrgPool(orgId);

				CreateOrgSlots(world, characterConfig, rng, orgId, "master", 1, pool, isPlayerOrg);

				int agentSlots = orgEntry.InitialAgentSlots;
				if (agentSlots > 0) {
					CreateOrgSlots(world, characterConfig, rng, orgId, "agent", agentSlots, pool, isPlayerOrg);
				}
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
						world.Add(skillEntity, new ResourceOwner(charId, OwnerType.Character));
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

		static void CreateActionEntities(World world, GameLogicContext context, Random rng, List<OrganizationEntry> participating) {
			var actionConfig = context.Action.Load();
			int handSize = actionConfig.GetHandSize("org");
			if (handSize <= 0) { return; }

			foreach (var orgEntry in participating) {
				string orgId = orgEntry.OrganizationId;
				var pool = actionConfig.GetOrgPool(orgId);
				if (pool == null || pool.Count == 0) { continue; }

				int deckEntity = world.Create();
				world.Add(deckEntity, new CardDeck { OrgId = orgId, CountryId = "" });
				world.Add(deckEntity, new CardHand { HandSize = handSize });

				var deckEntities = new List<int>();
				for (int i = 0; i < pool.Count; i++) {
					int cardEntity = world.Create();
					world.Add(cardEntity, new GameAction { ActionId = pool[i] });
					world.Add(cardEntity, new OrgContext { OrgId = orgId });
					deckEntities.Add(cardEntity);
				}

				for (int i = deckEntities.Count - 1; i > 0; i--) {
					int j = rng.Next(i + 1);
					var tmp = deckEntities[i]; deckEntities[i] = deckEntities[j]; deckEntities[j] = tmp;
				}
				for (int slot = 0; slot < handSize && slot < deckEntities.Count; slot++) {
					world.Add(deckEntities[slot], new CardInHand { SlotIndex = slot });
				}
			}
		}

		static void CreateCountryActionEntities(World world, GameLogicContext context, Random rng, List<OrganizationEntry> participating) {
			var actionConfig = context.Action.Load();
			var countryActions = new List<ActionDefinition>();
			foreach (var a in actionConfig.Actions) {
				if (a.OwnerType == "country") { countryActions.Add(a); }
			}
			if (countryActions.Count == 0) { return; }
			if (participating.Count == 0) { return; }
			var countryConfig = context.Country.Load();

			// Build char lookup: countryId -> roleId -> list of charIds
			var charsByCountryAndRole = new Dictionary<string, Dictionary<string, List<string>>>();
			int[] charReq = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(charReq, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					string cid = chars[i].CountryId;
					string rid = chars[i].RoleId;
					string charId = chars[i].CharacterId;
					if (string.IsNullOrEmpty(cid)) { continue; }
					if (!charsByCountryAndRole.TryGetValue(cid, out var byRole)) {
						byRole = new Dictionary<string, List<string>>();
						charsByCountryAndRole[cid] = byRole;
					}
					if (!byRole.TryGetValue(rid, out var list)) {
						list = new List<string>();
						byRole[rid] = list;
					}
					list.Add(charId);
				}
			}

			int handSize = actionConfig.GetHandSize("country");

			foreach (var entry in countryConfig.Countries) {
				if (!entry.IsAvailable) { continue; }

				foreach (var orgEntry in participating) {
					string orgId = orgEntry.OrganizationId;
					var createdEntities = new List<(int entity, string actionId)>();

					foreach (var def in countryActions) {
						// Determine targets
						var targets = new List<string>();
						if (def.TargetRole == "") {
							targets.Add("");
						} else {
							charsByCountryAndRole.TryGetValue(entry.CountryId, out var byRole);
							if (byRole != null && byRole.TryGetValue(def.TargetRole, out var charIds)) {
								targets.AddRange(charIds);
							}
						}

						for (int copyIndex = 0; copyIndex < def.DeckCopies; copyIndex++) {
							foreach (string targetCharId in targets) {
								int e = world.Create();
								world.Add(e, new GameAction { ActionId = def.ActionId });
								world.Add(e, new OrgContext { OrgId = orgId });
								world.Add(e, new CountryContext { CountryId = entry.CountryId });
								createdEntities.Add((e, def.ActionId));
							}
						}
					}

					int countryDeckEntity = world.Create();
					world.Add(countryDeckEntity, new CardDeck { OrgId = orgId, CountryId = entry.CountryId });
					world.Add(countryDeckEntity, new CardHand { HandSize = handSize });

					// Populate initial hand
					if (handSize > 0 && createdEntities.Count > 0) {
						int orgControl = GetOrgControlInCountry(world, orgId, entry.CountryId);
						var eligibleEntities = new List<int>();
						foreach (var (e, actionId) in createdEntities) {
							var d = actionConfig.Find(actionId);
							if (d == null) { continue; }
							bool eligible = true;
							var ctx = new ExpressionContext { Control = orgControl };
							foreach (var cond in d.Conditions) {
								if (ExpressionNode.Evaluate(cond, ctx) == 0.0) {
									eligible = false;
									break;
								}
							}
							if (eligible) { eligibleEntities.Add(e); }
						}

						// Fisher-Yates shuffle
						for (int i = eligibleEntities.Count - 1; i > 0; i--) {
							int j = rng.Next(i + 1);
							(eligibleEntities[i], eligibleEntities[j]) = (eligibleEntities[j], eligibleEntities[i]);
						}

						for (int slot = 0; slot < handSize && slot < eligibleEntities.Count; slot++) {
							world.Add(eligibleEntities[slot], new CardInHand { SlotIndex = slot });
						}
					}
				}
			}
		}

		static int GetOrgControlInCountry(World world, string orgId, string countryId) {
			int total = 0;
			int[] req = { TypeId<ControlEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ControlEffect[] effects = arch.GetColumn<ControlEffect>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (effects[i].OrgId == orgId && effects[i].CountryId == countryId) {
						total += effects[i].Value;
					}
				}
			}
			return total;
		}

		static void DiscoverInitialCountries(World world, List<OrganizationEntry> participating) {
			if (participating.Count == 0) { return; }

			var availableCountryIds = new HashSet<string>();
			int[] countryReq = { TypeId<Country>.Value };
			foreach (var arch in world.GetMatchingArchetypes(countryReq, null)) {
				Country[] countries = arch.GetColumn<Country>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					availableCountryIds.Add(countries[i].CountryId);
				}
			}

			foreach (var orgEntry in participating) {
				var toDiscover = new HashSet<string>();
				if (!string.IsNullOrEmpty(orgEntry.HqCountryId) && availableCountryIds.Contains(orgEntry.HqCountryId)) {
					toDiscover.Add(orgEntry.HqCountryId);
				}
				foreach (string countryId in toDiscover) {
					int entity = world.Create();
					world.Add(entity, new DiscoveredCountry { OrgId = orgEntry.OrganizationId, CountryId = countryId });
				}
			}
		}
	}
}
