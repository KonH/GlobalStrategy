using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class DiscoveryPerOrgTests {
		static int AddCountry(World world, string countryId) {
			int e = world.Create();
			world.Add(e, new Country(countryId));
			return e;
		}

		static void AddPlayer(World world, int countryEntity) {
			world.Add(countryEntity, new Player());
		}

		static int AddDiscoverEffect(World world, string orgId) {
			int e = world.Create();
			world.Add(e, new DiscoverCountryEffect { EffectId = "discover", OrgId = orgId });
			return e;
		}

		static HashSet<string> GetDiscoveredCountries(World world, string orgId) {
			var result = new HashSet<string>();
			int[] req = { TypeId<DiscoveredCountry>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				DiscoveredCountry[] dcs = arch.GetColumn<DiscoveredCountry>();
				for (int i = 0; i < arch.Count; i++) {
					if (dcs[i].OrgId == orgId) { result.Add(dcs[i].CountryId); }
				}
			}
			return result;
		}

		[Fact]
		void discover_effect_carries_acting_org_id() {
			var actionConfig = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "spy", OwnerType = "org", EffectIds = new List<string> { "discover" } }
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<GS.Game.Configs.ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" }
				}
			};

			var world = new World();
			int cardEntity = world.Create();
			world.Add(cardEntity, new GameAction { ActionId = "spy" });
			world.Add(cardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(cardEntity, new CardUse());
			world.Add(cardEntity, new ActionSucceeded());

			CreateActionEffectSystem.Update(world, actionConfig, effectConfig, DateTime.UtcNow);

			bool found = false;
			int[] req = { TypeId<DiscoverCountryEffect>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				DiscoverCountryEffect[] effects = arch.GetColumn<DiscoverCountryEffect>();
				for (int i = 0; i < arch.Count; i++) {
					if (effects[i].OrgId == "OrgA") { found = true; }
				}
			}
			Assert.True(found);
		}

		[Fact]
		void discovery_adds_country_to_acting_org_only() {
			var world = new World();
			AddCountry(world, "France");
			AddCountry(world, "Prussia");
			AddDiscoverEffect(world, "OrgA");

			var rng = new Random(1);
			DiscoverCountrySystem.Update(world, -1, rng, viewOrgId: "OrgA", hqCountryByOrgId: new Dictionary<string, string>());

			var discoveredA = GetDiscoveredCountries(world, "OrgA");
			var discoveredB = GetDiscoveredCountries(world, "OrgB");
			Assert.Single(discoveredA);
			Assert.Empty(discoveredB);
		}

		[Fact]
		void view_org_anchors_to_player_country_and_other_orgs_anchor_to_hq() {
			var world = new World();
			int playerCountryEntity = AddCountry(world, "PlayerHome");
			AddPlayer(world, playerCountryEntity);
			AddCountry(world, "NearPlayer");
			AddCountry(world, "NearOrgBHq");
			AddCountry(world, "OrgBHq");

			var pmEntity = world.Create();
			var distances = new Dictionary<(string, string), float> {
				[Order("PlayerHome", "NearPlayer")] = 0.001f,
				[Order("PlayerHome", "NearOrgBHq")] = 1000f,
				[Order("PlayerHome", "OrgBHq")] = 1000f,
				[Order("OrgBHq", "NearOrgBHq")] = 0.001f,
				[Order("OrgBHq", "NearPlayer")] = 1000f,
				[Order("NearPlayer", "NearOrgBHq")] = 1000f
			};
			world.Add(pmEntity, new ProximityMapData { Distances = distances });

			AddDiscoverEffect(world, "OrgA");
			AddDiscoverEffect(world, "OrgB");

			var hqByOrg = new Dictionary<string, string> { ["OrgA"] = "", ["OrgB"] = "OrgBHq" };
			var rng = new Random(42);
			DiscoverCountrySystem.Update(world, pmEntity, rng, viewOrgId: "OrgA", hqCountryByOrgId: hqByOrg);

			var discoveredA = GetDiscoveredCountries(world, "OrgA");
			var discoveredB = GetDiscoveredCountries(world, "OrgB");

			Assert.Single(discoveredA);
			Assert.Contains("NearPlayer", discoveredA);
			Assert.Single(discoveredB);
			Assert.Contains("NearOrgBHq", discoveredB);
		}

		static (string, string) Order(string a, string b) {
			return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
		}

		[Fact]
		void initial_discovery_is_per_org_hq_plus_player_country_for_view_org() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: participants,
				initialOrganizationId: MultiOrgTestSupport.OrgA,
				initialPlayerCountryId: MultiOrgTestSupport.ExtraCountry1);
			var logic = new GameLogic(ctx);
			logic.Update(0f);
			var world = logic.World;

			var discoveredA = GetDiscoveredCountries(world, MultiOrgTestSupport.OrgA);
			var discoveredB = GetDiscoveredCountries(world, MultiOrgTestSupport.OrgB);

			Assert.Equal(new HashSet<string> { MultiOrgTestSupport.HqA, MultiOrgTestSupport.ExtraCountry1 }, discoveredA);
			Assert.Equal(new HashSet<string> { MultiOrgTestSupport.HqB }, discoveredB);
		}

		[Fact]
		void visual_state_discovered_countries_sourced_from_view_org_set() {
			var participants = new List<string> { MultiOrgTestSupport.OrgA, MultiOrgTestSupport.OrgB };
			var ctx = MultiOrgTestSupport.BuildContext(
				participatingOrganizationIds: participants,
				initialOrganizationId: MultiOrgTestSupport.OrgA);
			var logic = new GameLogic(ctx);
			logic.Update(0f);

			int newEntity = logic.World.Create();
			logic.World.Add(newEntity, new DiscoveredCountry { OrgId = MultiOrgTestSupport.OrgB, CountryId = MultiOrgTestSupport.ExtraCountry2 });
			logic.Update(0f);

			Assert.DoesNotContain(MultiOrgTestSupport.ExtraCountry2, logic.VisualState.DiscoveredCountries.CountryIds);
			Assert.Contains(MultiOrgTestSupport.HqA, logic.VisualState.DiscoveredCountries.CountryIds);
		}
	}
}
