using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class DurabilityCollectorTests {
		[Fact]
		void compute_sums_base_plus_ruler_and_economic_stinginess() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "stinginess", 18);
			AddCharacterWithSkill(world, "eco_1", "France", "economic_advisor", "stinginess", 12);
			var bases = new Dictionary<string, int> { ["France"] = 82 };
			var collector = new DurabilityCollector(bases);

			double delta = collector.Compute("France", 0.0, world);

			Assert.Equal(112.0, delta, 6);
		}

		[Fact]
		void compute_treats_missing_character_or_skill_as_zero() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "stinginess", 18);
			var bases = new Dictionary<string, int> { ["France"] = 82 };
			var collector = new DurabilityCollector(bases);

			double delta = collector.Compute("France", 0.0, world);

			Assert.Equal(100.0, delta, 6);
		}

		[Fact]
		void compute_returns_delta_from_current_value() {
			var world = new World();
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "stinginess", 18);
			AddCharacterWithSkill(world, "eco_1", "France", "economic_advisor", "stinginess", 12);
			var bases = new Dictionary<string, int> { ["France"] = 82 };
			var collector = new DurabilityCollector(bases);

			double delta = collector.Compute("France", 50.0, world);

			Assert.Equal(62.0, delta, 6);
		}

		[Fact]
		void compute_uses_default_base_when_country_missing_from_dict() {
			var world = new World();
			var collector = new DurabilityCollector(new Dictionary<string, int>());

			double delta = collector.Compute("Unknown", 0.0, world);

			Assert.Equal(40.0, delta, 6);
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
