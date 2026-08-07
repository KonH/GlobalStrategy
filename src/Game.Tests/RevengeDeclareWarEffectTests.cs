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
				new ActionDefinition { ActionId = "declare_revenge_war", OwnerType = "country", EffectIds = new List<string> { "revenge_effect" } }
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

		static void RunEffect(
			World world,
			IReadOnlyDictionary<string, string>? hqCountryByOrgId,
			DateTime? time = null,
			GameSettings? settings = null) {
			CreateActionEffectSystem.Update(
				world, BuildActionConfig(), BuildEffectConfig(), time ?? CurrentTime,
				new Random(1), settings ?? new GameSettings(), new ProvinceTopology(new ProvinceConfig()),
				new Dictionary<string, (double Lon, double Lat)>(), 100, hqCountryByOrgId);
		}

		static int? FindWarBattleCapacity(World world) {
			int[] req = { TypeId<WarBattleCapacity>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				WarBattleCapacity[] capacities = arch.GetColumn<WarBattleCapacity>();
				if (arch.Count > 0) {
					return capacities[0].MaxConcurrentBattleCount;
				}
			}
			return null;
		}

		static void StopWar(World world, string countryId) {
			Wars.StopWar(
				world, countryId, CurrentTime.AddDays(1), new Random(1), new GameSettings(),
				new ProvinceTopology(new ProvinceConfig()),
				new Dictionary<string, (double Lon, double Lat)>(), 100);
		}

		static int AddSucceededCard(World world, string orgId, string countryId, string actionId, string targetCountryId = "") {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = actionId });
			world.Add(e, new OrgContext { OrgId = orgId });
			world.Add(e, new CountryContext { CountryId = countryId });
			if (!string.IsNullOrEmpty(targetCountryId)) { world.Add(e, new RevengeCardTarget { TargetCountryId = targetCountryId }); }
			world.Add(e, new CardUse { CountryId = countryId });
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
		void declares_war_and_attaches_bonus() {
			var world = new World();
			AddSucceededCard(world, OrgId, HqCountryId, "declare_revenge_war", TargetCountryId);
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };

			RunEffect(world, hqCountryByOrgId);

			Assert.True(Wars.IsInWar(world, HqCountryId));
			Assert.True(Wars.IsInWar(world, TargetCountryId));
			var bonus = FindBonus(world, HqCountryId);
			Assert.NotNull(bonus);
			Assert.Equal(10.0, bonus!.Value.DamageBonusPercent);
			Assert.Equal(5.0, bonus.Value.DurabilityBonusPercent);
			Assert.NotEqual("", bonus.Value.WarId);
			Assert.True(CountEntities<WarDeclaredApplied>(world) >= 1);
		}

		[Fact]
		void declare_uses_passed_war_battle_settings_for_capacity() {
			var world = new World();
			AddSucceededCard(world, OrgId, HqCountryId, "declare_revenge_war", TargetCountryId);
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };
			var settings = new GameSettings {
				WarBattles = new WarBattleSettings { BaseConcurrentBattleCount = 4 }
			};

			RunEffect(world, hqCountryByOrgId, settings: settings);

			Assert.Equal(4, FindWarBattleCapacity(world));
		}

		[Fact]
		void declares_war_without_an_org_hq() {
			var world = new World();
			AddSucceededCard(world, OrgId, HqCountryId, "declare_revenge_war", TargetCountryId);

			RunEffect(world, null);

			Assert.True(Wars.IsInWar(world, TargetCountryId));
			Assert.NotNull(FindBonus(world, HqCountryId));
		}

		[Fact]
		void no_ops_when_declare_war_itself_would_no_op() {
			var world = new World();
			Wars.DeclareWar(world, HqCountryId, "Germany", CurrentTime);
			AddSucceededCard(world, OrgId, HqCountryId, "declare_revenge_war", TargetCountryId);
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };

			RunEffect(world, hqCountryByOrgId);

			Assert.False(Wars.IsInWar(world, TargetCountryId));
			Assert.Null(FindBonus(world, HqCountryId));
			Assert.Equal(1, CountEntities<War>(world));
		}

		[Fact]
		void second_declare_replaces_leftover_bonus_instead_of_leaving_two_entities() {
			var world = new World();
			var hqCountryByOrgId = new Dictionary<string, string> { [OrgId] = HqCountryId };

			AddSucceededCard(world, OrgId, HqCountryId, "declare_revenge_war", TargetCountryId);
			RunEffect(world, hqCountryByOrgId);
			Assert.NotNull(FindBonus(world, HqCountryId));

			StopWar(world, HqCountryId);

			AddSucceededCard(world, OrgId, HqCountryId, "declare_revenge_war", "Germany");
			RunEffect(world, hqCountryByOrgId, CurrentTime.AddDays(1));

			Assert.Equal(1, CountEntities<RevengeWarBonus>(world));
			var secondBonus = FindBonus(world, HqCountryId);
			Assert.NotNull(secondBonus);
			Assert.Equal(10.0, secondBonus!.Value.DamageBonusPercent);
			Assert.Equal(5.0, secondBonus.Value.DurabilityBonusPercent);
		}
	}
}
