using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class TargetedResourceInitializationTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;

			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		readonly record struct ResourceEntry(string OwnerId, OwnerType OwnerType, string ResourceId, double Value);
		readonly record struct CollectorEntry(string OwnerId, OwnerType OwnerType, string ResourceId, PayType PayType, string CollectorId);

		static ResourceConfig BuildResourceConfig() {
			return new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.Gold,
						DefaultInitialValue = 100,
						DefaultEffects = new List<EffectDefinition> {
							new EffectDefinition { EffectId = "base_income", Value = 1, PayType = "Monthly" }
						}
					},
					new ResourceDefinition { ResourceId = ResourceDefinitions.CountryPopulation },
					new ResourceDefinition { ResourceId = ResourceDefinitions.CountryScore },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Recruits },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Population, SeedTarget = ResourceSeedTarget.Province },
					new ResourceDefinition { ResourceId = ResourceDefinitions.OrgScore, SeedTarget = ResourceSeedTarget.Org },
					new ResourceDefinition { ResourceId = "power", SeedTarget = ResourceSeedTarget.Character },
					new ResourceDefinition { ResourceId = "charm", SeedTarget = ResourceSeedTarget.Character },
					new ResourceDefinition { ResourceId = "stinginess", SeedTarget = ResourceSeedTarget.Character },
					new ResourceDefinition { ResourceId = "intrigue", SeedTarget = ResourceSeedTarget.Character }
				}
			};
		}

		static CharacterConfig BuildCharacterConfig() {
			var skillIds = new List<string> { "power", "charm", "stinginess", "intrigue" };
			return new CharacterConfig {
				Skills = skillIds.Select(skillId => new CharacterSkillDefinition { SkillId = skillId }).ToList(),
				Roles = new List<CharacterRoleDefinition> {
					new CharacterRoleDefinition { RoleId = "ruler", SkillIds = new List<string>(skillIds) },
					new CharacterRoleDefinition { RoleId = "master", SkillIds = new List<string>(skillIds) }
				},
				CountryPools = new List<CountryCharacterPool> {
					new CountryCharacterPool {
						CountryId = "country_a",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["ruler"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "country_character",
									Skills = BuildFixedSkills(11)
								}
							}
						}
					}
				},
				OrgPools = new List<OrgCharacterPool> {
					new OrgCharacterPool {
						OrgId = "org_a",
						Slots = new Dictionary<string, List<CharacterEntry>> {
							["master"] = new List<CharacterEntry> {
								new CharacterEntry {
									CharacterId = "org_character",
									Skills = BuildFixedSkills(22)
								}
							}
						}
					}
				}
			};
		}

		static Dictionary<string, SkillSettings> BuildFixedSkills(int value) {
			return new Dictionary<string, SkillSettings> {
				["power"] = new SkillSettings { MinValue = value, MaxValue = value },
				["charm"] = new SkillSettings { MinValue = value, MaxValue = value },
				["stinginess"] = new SkillSettings { MinValue = value, MaxValue = value },
				["intrigue"] = new SkillSettings { MinValue = value, MaxValue = value }
			};
		}

		static GameLogic BuildLogic(ResourceConfig? resourceConfig = null) {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = "country_a",
						IsAvailable = true,
						InitialResources = new List<CountryResourceInit> {
							new CountryResourceInit { ResourceId = ResourceDefinitions.Gold, Value = 250 }
						}
					}
				}
			};
			var organizationConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "org_a",
						HqCountryId = "country_a",
						InitialGold = 777,
						BaseControl = 10
					}
				}
			};
			var provinceConfig = new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "province_a", CountryId = "country_a", Population = 1000 }
				}
			};

			var context = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(new GameSettings()),
				new StaticConfig<ResourceConfig>(resourceConfig ?? BuildResourceConfig()),
				new StaticConfig<OrganizationConfig>(organizationConfig),
				initialOrganizationId: "org_a",
				character: new StaticConfig<CharacterConfig>(BuildCharacterConfig()),
				province: new StaticConfig<ProvinceConfig>(provinceConfig),
				rngSeed: 1);
			return new GameLogic(context);
		}

		static List<ResourceEntry> GetResources(World world) {
			var result = new List<ResourceEntry>();
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				Resource[] resources = archetype.GetColumn<Resource>();
				for (int i = 0; i < archetype.Count; i++) {
					result.Add(new ResourceEntry(owners[i].OwnerId, owners[i].OwnerType, resources[i].ResourceId, resources[i].Value));
				}
			}
			return result;
		}

		static List<CollectorEntry> GetCollectors(World world) {
			var result = new List<CollectorEntry>();
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value,
				TypeId<ResourceCollector>.Value
			};
			foreach (var archetype in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				ResourceLink[] links = archetype.GetColumn<ResourceLink>();
				ResourceEffect[] effects = archetype.GetColumn<ResourceEffect>();
				ResourceCollector[] collectors = archetype.GetColumn<ResourceCollector>();
				for (int i = 0; i < archetype.Count; i++) {
					result.Add(new CollectorEntry(
						owners[i].OwnerId, owners[i].OwnerType, links[i].ResourceId,
						effects[i].PayType, collectors[i].CollectorId));
				}
			}
			return result;
		}

		static ResourceEntry AssertSingleResource(
			IEnumerable<ResourceEntry> resources, string ownerId, OwnerType ownerType, string resourceId) {
			return Assert.Single(resources, resource =>
				resource.OwnerId == ownerId && resource.OwnerType == ownerType && resource.ResourceId == resourceId);
		}

		[Fact]
		void resources_seed_once_for_declared_targets_and_keep_specialized_values() {
			var logic = BuildLogic();

			logic.Update(0f);

			List<ResourceEntry> resources = GetResources(logic.World);
			string[] countryResourceIds = resources
				.Where(resource => resource.OwnerId == "country_a" && resource.OwnerType == OwnerType.Country)
				.Select(resource => resource.ResourceId)
				.OrderBy(resourceId => resourceId)
				.ToArray();
			Assert.Equal(new[] { "country_population", "country_score", "gold", "recruits" }, countryResourceIds);
			Assert.Equal(250, AssertSingleResource(resources, "country_a", OwnerType.Country, ResourceDefinitions.Gold).Value);
			Assert.Equal(1000, AssertSingleResource(resources, "country_a", OwnerType.Country, ResourceDefinitions.CountryPopulation).Value);
			Assert.Equal(1000, AssertSingleResource(resources, "country_a", OwnerType.Country, ResourceDefinitions.CountryScore).Value);
			Assert.Equal(50, AssertSingleResource(resources, "country_a", OwnerType.Country, ResourceDefinitions.Recruits).Value);

			Assert.Equal(1000, AssertSingleResource(resources, "province_a", OwnerType.Province, ResourceDefinitions.Population).Value);
			Assert.Equal(777, AssertSingleResource(resources, "org_a", OwnerType.Org, ResourceDefinitions.Gold).Value);
			Assert.Equal(100, AssertSingleResource(resources, "org_a", OwnerType.Org, ResourceDefinitions.OrgScore).Value);

			foreach (string characterId in new[] { "country_character", "org_character" }) {
				string[] skillIds = resources
					.Where(resource => resource.OwnerId == characterId && resource.OwnerType == OwnerType.Character)
					.Select(resource => resource.ResourceId)
					.OrderBy(resourceId => resourceId)
					.ToArray();
				Assert.Equal(new[] { "charm", "intrigue", "power", "stinginess" }, skillIds);
			}

			Assert.DoesNotContain(resources, resource =>
				resource.OwnerId == "org_a" && resource.ResourceId != ResourceDefinitions.Gold && resource.ResourceId != ResourceDefinitions.OrgScore);
			Assert.DoesNotContain(resources, resource =>
				resource.OwnerId == "province_a" && resource.ResourceId != ResourceDefinitions.Population);
		}

		[Fact]
		void target_specific_collectors_remain_singular_and_org_gold_has_no_generic_effect() {
			var logic = BuildLogic();

			logic.Update(0f);

			List<CollectorEntry> collectors = GetCollectors(logic.World);
			Assert.Single(collectors, collector =>
				collector.OwnerId == "province_a" && collector.OwnerType == OwnerType.Province &&
				collector.ResourceId == ResourceDefinitions.Population && collector.PayType == PayType.Monthly &&
				collector.CollectorId == PopulationGrowthCollector.Id);
			Assert.Single(collectors, collector =>
				collector.OwnerId == "country_a" && collector.OwnerType == OwnerType.Country &&
				collector.ResourceId == ResourceDefinitions.CountryPopulation && collector.PayType == PayType.Monthly &&
				collector.CollectorId == CountryPopulationCollector.Id);
			Assert.Single(collectors, collector =>
				collector.OwnerId == "country_a" && collector.OwnerType == OwnerType.Country &&
				collector.ResourceId == ResourceDefinitions.CountryScore && collector.PayType == PayType.Monthly &&
				collector.CollectorId == CountryScoreCollector.Id);
			Assert.Single(collectors, collector =>
				collector.OwnerId == "country_a" && collector.OwnerType == OwnerType.Country &&
				collector.ResourceId == ResourceDefinitions.Recruits && collector.PayType == PayType.Monthly &&
				collector.CollectorId == RecruitsGrowthCollector.Id);
			Assert.Single(collectors, collector =>
				collector.OwnerId == "org_a" && collector.OwnerType == OwnerType.Org &&
				collector.ResourceId == ResourceDefinitions.OrgScore && collector.PayType == PayType.Daily &&
				collector.CollectorId == OrgScoreCollector.Id);

			int[] required = { TypeId<ResourceOwner>.Value, TypeId<ResourceLink>.Value, TypeId<ResourceEffect>.Value };
			int countryGoldEffectCount = 0;
			foreach (var archetype in logic.World.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				ResourceLink[] links = archetype.GetColumn<ResourceLink>();
				ResourceEffect[] effects = archetype.GetColumn<ResourceEffect>();
				for (int i = 0; i < archetype.Count; i++) {
					Assert.False(owners[i].OwnerId == "org_a" && links[i].ResourceId == ResourceDefinitions.Gold);
					if (owners[i].OwnerId == "country_a" && links[i].ResourceId == ResourceDefinitions.Gold) {
						Assert.Equal(OwnerType.Country, owners[i].OwnerType);
						Assert.Equal("base_income", effects[i].EffectId);
						Assert.Equal(PayType.Monthly, effects[i].PayType);
						countryGoldEffectCount++;
					}
				}
			}
			Assert.Equal(1, countryGoldEffectCount);
		}

		[Fact]
		void dynamic_opinion_resource_remains_runtime_created_for_country_character() {
			var logic = BuildLogic();
			logic.Update(0f);

			Assert.DoesNotContain(GetResources(logic.World), resource => resource.ResourceId == "opinion_org_a");

			logic.Commands.Push(new DebugImproveOpinionCommand { CountryId = "country_a", OrgId = "org_a" });
			logic.Update(0f);

			ResourceEntry opinion = AssertSingleResource(
				GetResources(logic.World), "country_character", OwnerType.Character, "opinion_org_a");
			Assert.Equal(50, opinion.Value);
		}

		[Theory]
		[InlineData(ResourceSeedTarget.Country)]
		[InlineData(ResourceSeedTarget.Province)]
		[InlineData(ResourceSeedTarget.Org)]
		[InlineData(ResourceSeedTarget.Character)]
		void unsupported_static_resource_target_pair_fails_with_context(ResourceSeedTarget seedTarget) {
			var resourceConfig = BuildResourceConfig();
			resourceConfig.Resources.Add(new ResourceDefinition {
				ResourceId = "unsupported_resource",
				SeedTarget = seedTarget
			});
			var logic = BuildLogic(resourceConfig);

			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => logic.Update(0f));

			Assert.Contains("unsupported_resource", exception.Message);
			Assert.Contains(seedTarget.ToString(), exception.Message);
		}
	}
}
