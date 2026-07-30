using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class RevengeDeclareWarEffectTests {
		const string OrgId = "OrgA";
		const string HqCountryId = "Great_Britain";
		const string TargetCountryId = "France";
		static readonly DateTime CurrentTime = new DateTime(1880, 1, 1);

		static ActionConfig BuildActionConfig() => new ActionConfig {
			Actions = new List<ActionDefinition> {
				new ActionDefinition { ActionId = "revenge", OwnerType = "country", EffectIds = new List<string> { "revenge_effect" } }
			}
		};

		static EffectConfig BuildEffectConfig() => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new DeclareRevengeWarEffectParams {
					EffectId = "revenge_effect",
					EffectType = "DeclareRevengeWar",
					DamageBonusPercent = 10.0,
					DurabilityBonusPercent = 5.0
				}
			}
		};

		static int AddSucceededCard(World world, string orgId, string countryId, string actionId) {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			world.Add(e, new CountryContext { CountryId = countryId });
			world.Add(e, new CardUse());
			world.Add(e, new ActionSucceeded());
			return e;
		}

		static RevengeWarBonus? FindBonus(World world, string countryId) {
			int[] req = { TypeId<RevengeWarBonus>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				RevengeWarBonus[] bonuses = arch.GetColumn<RevengeWarBonus>();
				for (int i = 0; i < arch.Count; i++) {
					if (bonuses[i].CountryId == countryId) { return bonuses[i]; }
				}
			}
			return null;
		}

		static int CountEntities<T>(World world) {
			int count = 0;
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) { count += arch.Count; }
			return count;
		}

		[Fact]
		void declares_war_attaches_bonus_and_returns_true() {
			var world = new World();
			AddSucceededCard(world, OrgId, TargetCountryId, "revenge");
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };

			bool result = CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime, hqCountryByOrgId);

			Assert.True(result);
			Assert.True(Wars.IsInWar(world, HqCountryId));
			Assert.True(Wars.IsInWar(world, TargetCountryId));
			var bonus = FindBonus(world, HqCountryId);
			Assert.NotNull(bonus);
			Assert.Equal(10.0, bonus!.Value.DamageBonusPercent);
			Assert.Equal(5.0, bonus.Value.DurabilityBonusPercent);
			Assert.NotEqual("", bonus.Value.WarId);
		}

		[Fact]
		void returns_false_and_no_war_when_hq_country_by_org_id_is_null() {
			var world = new World();
			AddSucceededCard(world, OrgId, TargetCountryId, "revenge");

			bool result = CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime, null);

			Assert.False(result);
			Assert.False(Wars.IsInWar(world, TargetCountryId));
			Assert.Null(FindBonus(world, HqCountryId));
		}

		[Fact]
		void returns_false_and_no_war_when_hq_country_by_org_id_missing_the_org() {
			var world = new World();
			AddSucceededCard(world, OrgId, TargetCountryId, "revenge");
			var hqCountryByOrgId = new Dictionary<string, string> { ["SomeOtherOrg"] = HqCountryId };

			bool result = CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime, hqCountryByOrgId);

			Assert.False(result);
			Assert.False(Wars.IsInWar(world, TargetCountryId));
			Assert.Null(FindBonus(world, HqCountryId));
		}

		[Fact]
		void no_ops_when_declare_war_itself_would_no_op() {
			var world = new World();
			Wars.DeclareWar(world, HqCountryId, "Germany", CurrentTime);
			AddSucceededCard(world, OrgId, TargetCountryId, "revenge");
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };

			bool result = CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime, hqCountryByOrgId);

			Assert.False(result);
			Assert.False(Wars.IsInWar(world, TargetCountryId));
			Assert.Null(FindBonus(world, HqCountryId));
			Assert.Equal(1, CountEntities<War>(world));
		}

		[Fact]
		void second_declare_replaces_leftover_bonus_instead_of_leaving_two_entities() {
			var world = new World();
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };

			AddSucceededCard(world, OrgId, TargetCountryId, "revenge");
			CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime, hqCountryByOrgId);
			var firstBonus = FindBonus(world, HqCountryId);
			Assert.NotNull(firstBonus);

			// War ends by any means (e.g. debug stop-war) but the bonus is not destroyed —
			// only decay ever touches it, per the spec's "decay never ends the war" precedent.
			Wars.StopWar(world, HqCountryId);

			AddSucceededCard(world, OrgId, "Germany", "revenge");
			bool result = CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime.AddDays(1), hqCountryByOrgId);

			Assert.True(result);
			Assert.Equal(1, CountEntities<RevengeWarBonus>(world));
			var secondBonus = FindBonus(world, HqCountryId);
			Assert.NotNull(secondBonus);
			Assert.Equal(10.0, secondBonus!.Value.DamageBonusPercent);
			Assert.Equal(5.0, secondBonus.Value.DurabilityBonusPercent);
		}
	}
}
