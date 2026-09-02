using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Main {
	static class InitSystem {
		public static bool Update(
			World world, GameLogicContext context, Random rng, ResourceQuery resources, CountryRelations relations) {
			int[] required = { TypeId<IsInitialized>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return false;
				}
			}
			Run(world, context, rng, resources, relations);
			return true;
		}

		static void Run(World world, GameLogicContext context, Random rng, ResourceQuery resources, CountryRelations relations) {
			var countryConfig = context.Country.Load();
			var resourceConfig = context.Resource.Load();

			foreach (var entry in countryConfig.Countries) {
				if (!entry.IsAvailable) {
					continue;
				}
				int entity = world.Create();
				world.Add(entity, new Country(entry.CountryId));
				CreateCountryResourceEntities(world, resources, entry, resourceConfig);
			}

			var settings = context.GameSettings.Load();
			var enableSecretAdvisor = settings.FeatureFlags.EnableSecretAdvisor;
			var enableRuler = settings.FeatureFlags.EnableRuler;
			var enableFriendsRelation = settings.FeatureFlags.EnableFriendsRelation;
			var enableForceWarCards = settings.FeatureFlags.EnableForceWarCards;

			int countryRelationsVersionEntity = world.Create();
			world.Add(countryRelationsVersionEntity, new CountryRelationsVersion { Value = 0 });

			SeedCountryRelations(world, relations, countryConfig, enableFriendsRelation);

			int relationCardSyncStateEntity = world.Create();
			world.Add(relationCardSyncStateEntity, new RelationCardSyncState { LastSyncedVersion = -1 });

			// ProvinceOwnership is not seeded here — InitSystem only creates raw entity data,
			// it does not call into other systems. GameLogic.Update seeds it via
			// ProvinceOwnershipSystem.Seed once, gated by this same Update()'s IsInitialized
			// return flag. See ecs_patterns.md's "no system-to-system calls" rule.
			var provinceConfig = context.Province.Load();
			CreateProvinceResourceEntities(world, resources, provinceConfig, resourceConfig);

			var startTime = new DateTime(settings.StartYear, 1, 1);

			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime {
				CurrentTime = startTime,
				IsPaused = false,
				MultiplierIndex = 0
			});

			string locale = string.IsNullOrEmpty(context.InitialLocale) ? settings.DefaultLocale : context.InitialLocale;

			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = locale });

			int settingsEntity = world.Create();
			world.Add(settingsEntity, new AppSettings {
				Locale = locale,
				AutoSaveInterval = ParseAutoSaveInterval(settings.AutoSaveInterval)
			});

			int botActionLogEntity = world.Create();
			world.Add(botActionLogEntity, new BotActionLog { Entries = Array.Empty<string>() });

			var orgConfig = context.Organization.Load();
			var participating = ResolveParticipatingOrgs(context, orgConfig);

			int completionEntity = world.Create();
			world.Add(completionEntity, new GameCompletion {
				IsCompleted = false,
				WinnerOrganizationId = ""
			});

			for (int participationOrder = 0; participationOrder < participating.Count; participationOrder++) {
				var orgEntry = participating[participationOrder];
				int orgEntity = world.Create();
				world.Add(orgEntity, new Organization {
					OrganizationId = orgEntry.OrganizationId,
					DisplayName = orgEntry.DisplayName
				});
				world.Add(orgEntity, new OrganizationGameOutcome {
					ParticipationOrder = participationOrder,
					Result = OrganizationGameResult.InProgress
				});
				if (orgEntry.OrganizationId != context.InitialOrganizationId) {
					world.Add(orgEntity, new BotControlled());
				} else {
					world.Add(orgEntity, new Player());
				}

				// Organization gold is a specialized exception: its value comes from
				// OrganizationEntry even though generic gold is country-targeted.
				// Dynamic opinion_<orgId> resources are likewise not dispatched here;
				// actions and save loading create those runtime-only IDs explicitly.
				resources.Set(world, orgEntry.OrganizationId, ResourceDefinitions.Gold, orgEntry.InitialGold);

				CreateOrgResourceEntities(world, resources, orgEntry.OrganizationId, resourceConfig);

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
			CreateOrgCharacterEntities(world, context, resourceConfig, rng, participating, resources);
			CreateCharacterEntities(world, context, resourceConfig, rng, enableSecretAdvisor, enableRuler, resources);
			CreateCountryActionEntities(world, context, participating, enableFriendsRelation, enableSecretAdvisor, enableForceWarCards, countryConfig);

			// InitSystem does not call ResourceSystem.Update itself — it only creates the raw
			// Resource/ResourceEffect/ResourceCollector entities above. GameLogic.Update calls
			// ResourceSystem.Update unconditionally on every tick, including this same tick right
			// after InitSystem.Update returns, which seeds every collector-driven resource
			// (country_population/country_score/recruits/org_score/damage/durability) via their Instant effects.
			// Instant effects apply regardless of month/day-boundary state and self-destroy after
			// firing once, so they are their own "already initialized" marker — no separate
			// bootstrap call or flag is needed. See ecs_patterns.md's "no system-to-system calls"
			// rule, Docs/Specs/26_07_18_17_resource-collector-pipeline/plan.md and
			// Docs/Specs/26_07_18_15_recruits-resource/plan.md.

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

		static void CreateCharacterEntities(
			World world, GameLogicContext context, ResourceConfig resourceConfig, Random rng, bool enableSecretAdvisor,
			bool enableRuler, ResourceQuery resources) {
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
					if (role.RoleId == "secret_advisor" && !enableSecretAdvisor) {
						continue;
					}
					if (role.RoleId == "ruler" && !enableRuler) {
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
					CreateCharacterResourceEntities(
						world, resources, resourceConfig, characterConfig, charEntry, rng, charEntry.CharacterId, null);
				}
			}
		}

		static void SeedCountryRelations(
			World world, CountryRelations relations, CountryConfig config, bool enableFriendsRelation) {
			var availableCountryIds = new HashSet<string>();
			foreach (var entry in config.Countries) {
				if (entry.IsAvailable) {
					availableCountryIds.Add(entry.CountryId);
				}
			}

			var seenPairs = new HashSet<(string, string)>();
			foreach (var entry in config.Countries) {
				if (!entry.IsAvailable) {
					continue;
				}
				if (enableFriendsRelation) {
					SeedRelationsForEntry(world, relations, entry.CountryId, entry.HistoricalFriends, RelationKind.Friend, availableCountryIds, seenPairs);
				}
				SeedRelationsForEntry(world, relations, entry.CountryId, entry.HistoricalRivals, RelationKind.Rival, availableCountryIds, seenPairs);
			}
		}

		static void SeedRelationsForEntry(
			World world, CountryRelations relations, string countryId, List<string> otherIds, RelationKind kind,
			HashSet<string> availableCountryIds, HashSet<(string, string)> seenPairs) {
			foreach (var otherId in otherIds) {
				if (otherId == countryId || !availableCountryIds.Contains(otherId)) {
					continue;
				}
				string a = countryId;
				string b = otherId;
				if (string.CompareOrdinal(a, b) > 0) {
					(a, b) = (b, a);
				}
				if (!seenPairs.Add((a, b))) {
					continue;
				}
				relations.SetRelation(world, a, b, kind);
			}
		}

		static void AttachRecruitsEffects(World world, CountryEntry entry) {
			int seedEffectEntity = world.Create();
			world.Add(seedEffectEntity, new ResourceOwner(entry.CountryId, OwnerType.Country));
			world.Add(seedEffectEntity, new ResourceLink(ResourceDefinitions.Recruits));
			world.Add(seedEffectEntity, new ResourceEffect {
				EffectId = $"recruits_seed_{entry.CountryId}",
				PayType = PayType.Instant
			});
			world.Add(seedEffectEntity, new ResourceCollector { CollectorId = RecruitsSeedCollector.Id });

			int growthEffectEntity = world.Create();
			world.Add(growthEffectEntity, new ResourceOwner(entry.CountryId, OwnerType.Country));
			world.Add(growthEffectEntity, new ResourceLink(ResourceDefinitions.Recruits));
			world.Add(growthEffectEntity, new ResourceEffect {
				EffectId = $"recruits_growth_{entry.CountryId}",
				PayType = PayType.Monthly
			});
			world.Add(growthEffectEntity, new ResourceCollector { CollectorId = RecruitsGrowthCollector.Id });
		}

		static void AttachCollectorDrivenCountryEffects(World world, string countryId, string resourceId, string collectorId) {
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

		static void AttachInstantDailyCountryEffects(World world, string countryId, string resourceId, string collectorId) {
			int instantEffectEntity = world.Create();
			world.Add(instantEffectEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(instantEffectEntity, new ResourceLink(resourceId));
			world.Add(instantEffectEntity, new ResourceEffect {
				EffectId = $"{resourceId}_seed_{countryId}",
				PayType = PayType.Instant
			});
			world.Add(instantEffectEntity, new ResourceCollector { CollectorId = collectorId });

			int dailyEffectEntity = world.Create();
			world.Add(dailyEffectEntity, new ResourceOwner(countryId, OwnerType.Country));
			world.Add(dailyEffectEntity, new ResourceLink(resourceId));
			world.Add(dailyEffectEntity, new ResourceEffect {
				EffectId = $"{resourceId}_daily_{countryId}",
				PayType = PayType.Daily
			});
			world.Add(dailyEffectEntity, new ResourceCollector { CollectorId = collectorId });
		}

		static void AttachOrgScoreEffects(World world, string orgId) {
			int instantEffectEntity = world.Create();
			world.Add(instantEffectEntity, new ResourceOwner(orgId));
			world.Add(instantEffectEntity, new ResourceLink(ResourceDefinitions.OrgScore));
			world.Add(instantEffectEntity, new ResourceEffect {
				EffectId = $"org_score_seed_{orgId}",
				PayType = PayType.Instant
			});
			world.Add(instantEffectEntity, new ResourceCollector { CollectorId = OrgScoreCollector.Id });

			int dailyEffectEntity = world.Create();
			world.Add(dailyEffectEntity, new ResourceOwner(orgId));
			world.Add(dailyEffectEntity, new ResourceLink(ResourceDefinitions.OrgScore));
			world.Add(dailyEffectEntity, new ResourceEffect {
				EffectId = $"org_score_daily_{orgId}",
				PayType = PayType.Daily
			});
			world.Add(dailyEffectEntity, new ResourceCollector { CollectorId = OrgScoreCollector.Id });
		}

		static void CreateProvinceResourceEntities(
			World world, ResourceQuery resources, ProvinceConfig config, ResourceConfig resourceConfig) {
			foreach (var entry in config.Provinces) {
				foreach (var resourceDef in resourceConfig.FindResources(ResourceSeedTarget.Province)) {
					if (resourceDef.ResourceId != ResourceDefinitions.Population) {
						ThrowUnsupportedResource(resourceDef);
					}
					resources.Set(world, entry.ProvinceId, resourceDef.ResourceId, entry.Population, OwnerType.Province);

					int growthEffectEntity = world.Create();
					world.Add(growthEffectEntity, new ResourceOwner(entry.ProvinceId, OwnerType.Province));
					world.Add(growthEffectEntity, new ResourceLink(ResourceDefinitions.Population));
					world.Add(growthEffectEntity, new ResourceEffect {
						EffectId = $"population_growth_{entry.ProvinceId}",
						PayType = PayType.Monthly
					});
					world.Add(growthEffectEntity, new ResourceCollector { CollectorId = PopulationGrowthCollector.Id });
				}
			}
		}

		static void CreateCountryResourceEntities(
			World world, ResourceQuery resources, CountryEntry entry, ResourceConfig resourceConfig) {
			foreach (var resourceDef in resourceConfig.FindResources(ResourceSeedTarget.Country)) {
				double initialValue = resourceDef.DefaultInitialValue;
				if (resourceDef.ResourceId == ResourceDefinitions.CountryPopulation ||
					resourceDef.ResourceId == ResourceDefinitions.CountryScore ||
					resourceDef.ResourceId == ResourceDefinitions.Recruits ||
					resourceDef.ResourceId == ResourceDefinitions.Damage ||
					resourceDef.ResourceId == ResourceDefinitions.Durability ||
					resourceDef.ResourceId == ResourceDefinitions.WarInitiative) {
					initialValue = 0;
				} else if (resourceDef.ResourceId != ResourceDefinitions.Gold &&
					resourceDef.ResourceId != ResourceDefinitions.TroopsDamageBonusPercent) {
					ThrowUnsupportedResource(resourceDef);
				}
				foreach (var init in entry.InitialResources) {
					if (init.ResourceId == resourceDef.ResourceId) {
						initialValue = init.Value;
						break;
					}
				}

				resources.Set(world, entry.CountryId, resourceDef.ResourceId, initialValue, OwnerType.Country);

				if (resourceDef.ResourceId == ResourceDefinitions.CountryPopulation) {
					AttachCollectorDrivenCountryEffects(world, entry.CountryId, resourceDef.ResourceId, CountryPopulationCollector.Id);
				} else if (resourceDef.ResourceId == ResourceDefinitions.CountryScore) {
					AttachCollectorDrivenCountryEffects(world, entry.CountryId, resourceDef.ResourceId, CountryScoreCollector.Id);
				} else if (resourceDef.ResourceId == ResourceDefinitions.Recruits) {
					AttachRecruitsEffects(world, entry);
				} else if (resourceDef.ResourceId == ResourceDefinitions.Damage) {
					AttachInstantDailyCountryEffects(world, entry.CountryId, resourceDef.ResourceId, DamageCollector.Id);
				} else if (resourceDef.ResourceId == ResourceDefinitions.Durability) {
					AttachInstantDailyCountryEffects(world, entry.CountryId, resourceDef.ResourceId, DurabilityCollector.Id);
				}

				foreach (var effectDef in resourceDef.DefaultEffects) {
					int effectEntity = world.Create();
					world.Add(effectEntity, new ResourceOwner(entry.CountryId, OwnerType.Country));
					world.Add(effectEntity, new ResourceLink(resourceDef.ResourceId));
					world.Add(effectEntity, new ResourceEffect {
						EffectId = effectDef.EffectId,
						Value = effectDef.Value,
						PayType = Enum.Parse<PayType>(effectDef.PayType, ignoreCase: true)
					});
					if (!string.IsNullOrEmpty(effectDef.CollectorId)) {
						world.Add(effectEntity, new ResourceCollector { CollectorId = effectDef.CollectorId });
					}
				}
			}
		}

		static void CreateOrgResourceEntities(World world, ResourceQuery resources, string orgId, ResourceConfig resourceConfig) {
			foreach (var resourceDef in resourceConfig.FindResources(ResourceSeedTarget.Org)) {
				if (resourceDef.ResourceId != ResourceDefinitions.OrgScore) {
					ThrowUnsupportedResource(resourceDef);
				}
				resources.Set(world, orgId, resourceDef.ResourceId, 0);
				AttachOrgScoreEffects(world, orgId);
			}
		}

		static void CreateOrgCharacterEntities(
			World world, GameLogicContext context, ResourceConfig resourceConfig, Random rng,
			List<OrganizationEntry> participating, ResourceQuery resources) {
			var characterConfig = context.Character.Load();

			foreach (var orgEntry in participating) {
				string orgId = orgEntry.OrganizationId;
				bool isPlayerOrg = true;
				var pool = characterConfig.FindOrgPool(orgId);

				CreateOrgSlots(world, resources, resourceConfig, characterConfig, rng, orgId, "master", 1, pool, isPlayerOrg);

				int agentSlots = orgEntry.InitialAgentSlots;
				if (agentSlots > 0) {
					CreateOrgSlots(world, resources, resourceConfig, characterConfig, rng, orgId, "agent", agentSlots, pool, isPlayerOrg);
				}
			}
		}

		static void CreateOrgSlots(
			World world, ResourceQuery resources, ResourceConfig resourceConfig, CharacterConfig characterConfig, Random rng,
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

					CreateCharacterResourceEntities(
						world, resources, resourceConfig, characterConfig, charEntry, rng, charId, roleSkillIds);
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

		static void CreateCharacterResourceEntities(
			World world, ResourceQuery resources, ResourceConfig resourceConfig, CharacterConfig characterConfig,
			CharacterEntry characterEntry, Random rng, string characterId, HashSet<string>? allowedSkillIds) {
			foreach (var resourceDef in resourceConfig.FindResources(ResourceSeedTarget.Character)) {
				var skillDef = characterConfig.FindSkill(resourceDef.ResourceId);
				if (skillDef == null) {
					ThrowUnsupportedResource(resourceDef);
				}
				if (allowedSkillIds != null && !allowedSkillIds.Contains(resourceDef.ResourceId)) {
					continue;
				}
				int skillValue;
				if (characterEntry.Skills.TryGetValue(resourceDef.ResourceId, out var settings)) {
					skillValue = rng.Next(settings.MinValue, settings.MaxValue + 1);
				} else {
					skillValue = rng.Next(5, 31);
				}
				resources.Set(world, characterId, resourceDef.ResourceId, skillValue, OwnerType.Character);
			}
		}

		static void ThrowUnsupportedResource(ResourceDefinition resourceDef) {
			throw new InvalidOperationException(
				$"Resource '{resourceDef.ResourceId}' has unsupported seed target '{resourceDef.SeedTarget}'.");
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
				world.Add(deckEntity, new CardDeck { OrgId = orgId });
				world.Add(deckEntity, new CardOwnerType(CardOwnerKind.Org));
				world.Add(deckEntity, new CardHand { HandSize = handSize });

				var deckEntities = new List<int>();
				for (int i = 0; i < pool.Count; i++) {
					int cardEntity = world.Create();
					world.Add(cardEntity, new GameAction { ActionId = pool[i] });
					world.Add(cardEntity, new OrgContext { OrgId = orgId });
					world.Add(cardEntity, new CardOwnerType(CardOwnerKind.Org));
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

		static void CreateCountryActionEntities(
			World world,
			GameLogicContext context,
			List<OrganizationEntry> participating,
			bool enableFriendsRelation,
			bool enableSecretAdvisor,
			bool enableForceWarCards,
			CountryConfig countryConfig) {
			var actionConfig = context.Action.Load();
			var countryActions = new List<ActionDefinition>();
			foreach (var a in actionConfig.Actions) {
				if (a.OwnerType == "country") { countryActions.Add(a); }
			}
			if (countryActions.Count == 0) { return; }
			if (participating.Count == 0) { return; }

			var availableTargetRoles = new HashSet<string>();
			int[] charReq = { TypeId<Character>.Value };
			foreach (var arch in world.GetMatchingArchetypes(charReq, null)) {
				Character[] chars = arch.GetColumn<Character>();
				int count = arch.Count;
				for (int i = 0; i < count; i++) {
					if (!string.IsNullOrEmpty(chars[i].CountryId) && !string.IsNullOrEmpty(chars[i].RoleId)) {
						availableTargetRoles.Add(chars[i].RoleId);
					}
				}
			}

			int handSize = actionConfig.GetHandSize("country");
			foreach (var orgEntry in participating) {
				string orgId = orgEntry.OrganizationId;
				int countryDeckEntity = world.Create();
				world.Add(countryDeckEntity, new CardDeck { OrgId = orgId });
				world.Add(countryDeckEntity, new CardOwnerType(CardOwnerKind.Country));
				world.Add(countryDeckEntity, new CardHand { HandSize = handSize });

				foreach (var def in countryActions) {
					if (def.Chance <= 0
						|| RelationCardSyncSystem.IsSyncedAction(def.ActionId)
						|| def.ActionId == "declare_revenge_war") {
						continue;
					}
					if (def.ActionId == "make_friend" && !enableFriendsRelation) { continue; }
					if (def.TargetRole == "secret_advisor" && !enableSecretAdvisor) { continue; }
					if ((def.ActionId == "force_war_win" || def.ActionId == "force_war_loss") && !enableForceWarCards) { continue; }
					if (!string.IsNullOrEmpty(def.TargetRole) && !availableTargetRoles.Contains(def.TargetRole)) { continue; }

					if (def.ActionId == "make_friend" || def.ActionId == "make_rival") {
						RelationKind kind = def.ActionId == "make_friend" ? RelationKind.Friend : RelationKind.Rival;
						foreach (var targetEntry in countryConfig.Countries) {
							if (!targetEntry.IsAvailable) { continue; }
							// No self-exclusion here — a country targeting itself is always
							// unplayable via CountryActionConditionContext.Build's guard, for
							// whichever org/country ends up being selected.
							int targetEntity = world.Create();
							world.Add(targetEntity, new GameAction { ActionId = def.ActionId });
							world.Add(targetEntity, new OrgContext { OrgId = orgId });
							world.Add(targetEntity, new CardOwnerType(CardOwnerKind.Country));
							world.Add(targetEntity, new RelationCardTarget { TargetCountryId = targetEntry.CountryId, Kind = kind });
						}
						continue;
					}

					int entity = world.Create();
					world.Add(entity, new GameAction { ActionId = def.ActionId });
					world.Add(entity, new OrgContext { OrgId = orgId });
					world.Add(entity, new CardOwnerType(CardOwnerKind.Country));
				}
			}
		}

	}
}
