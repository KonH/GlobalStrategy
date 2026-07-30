using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class DamageCollectorTests {
		[Fact]
		void compute_sums_base_plus_ruler_and_military_power() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "power", 20);
			AddCharacterWithSkill(world, "mil_1", "France", "military_advisor", "power", 15);
			var bases = new Dictionary<string, CountryCombatBases> {
				["France"] = new CountryCombatBases(85, 40)
			};
			var collector = new DamageCollector(bases);

			double delta = collector.Compute("France", 0.0, world);

			Assert.Equal(120.0, delta, 6);
		}

		[Fact]
		void compute_treats_missing_character_or_skill_as_zero() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "power", 20);
			var bases = new Dictionary<string, CountryCombatBases> {
				["France"] = new CountryCombatBases(85, 40)
			};
			var collector = new DamageCollector(bases);

			double delta = collector.Compute("France", 0.0, world);

			Assert.Equal(105.0, delta, 6);
		}

		[Fact]
		void compute_returns_delta_from_current_value() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "power", 20);
			AddCharacterWithSkill(world, "mil_1", "France", "military_advisor", "power", 15);
			var bases = new Dictionary<string, CountryCombatBases> {
				["France"] = new CountryCombatBases(85, 40)
			};
			var collector = new DamageCollector(bases);

			double delta = collector.Compute("France", 40.0, world);

			Assert.Equal(80.0, delta, 6);
		}

		[Fact]
		void compute_uses_default_base_when_country_missing_from_dict() {
			var world = new World();
			var collector = new DamageCollector(new Dictionary<string, CountryCombatBases>());

			double delta = collector.Compute("Unknown", 0.0, world);

			Assert.Equal(40.0, delta, 6);
		}

		[Fact]
		void compute_applies_revenge_bonus_percent_multiplicatively() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "power", 20);
			AddCharacterWithSkill(world, "mil_1", "France", "military_advisor", "power", 15);
			int be = world.Create();
			world.Add(be, new RevengeWarBonus { WarId = "war_1", CountryId = "France", DamageBonusPercent = 10.0, DurabilityBonusPercent = 5.0 });
			var bases = new Dictionary<string, CountryCombatBases> {
				["France"] = new CountryCombatBases(85, 40)
			};
			var collector = new DamageCollector(bases);

			double delta = collector.Compute("France", 0.0, world);

			Assert.Equal(120.0 * 1.10, delta, 6);
		}

		[Fact]
		void compute_bonus_is_zero_when_no_revenge_bonus_component() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "power", 20);
			AddCharacterWithSkill(world, "mil_1", "France", "military_advisor", "power", 15);
			var bases = new Dictionary<string, CountryCombatBases> {
				["France"] = new CountryCombatBases(85, 40)
			};
			var collector = new DamageCollector(bases);

			double delta = collector.Compute("France", 0.0, world);

			Assert.Equal(120.0, delta, 6);
		}

		static void AddCharacterWithSkill(
			World world, string characterId, string countryId, string roleId, string skillId, double skillValue) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = characterId,
				CountryId = countryId,
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = Array.Empty<string>()
			});
			int skillEntity = world.Create();
			world.Add(skillEntity, new ResourceOwner(characterId, OwnerType.Character));
			world.Add(skillEntity, new Resource { ResourceId = skillId, Value = skillValue });
		}
	}
}
