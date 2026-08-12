using System.Collections.Generic;

namespace GS.Game.ConsoleRunner.WarSim {
	// One row of the requested baseline matrix (see Docs conversation "war feature" research):
	// isolates a single asymmetry at a time (recruits count, damage skill, or durability skill)
	// so its effect on war duration/battle count/occupation/losses can be read independently.
	public class WarScenarioSpec {
		public string Name = "";
		// Total population for the side, spread evenly across that side's *real* province count
		// (France has 65 provinces, Russian_Empire has 150 — see WarScenarioRunner.LoadRealTopology)
		// rather than a fixed per-province figure, so "equal opponents" stays force-equal despite the
		// two countries' very different real province counts.
		public double AttackerTotalPopulation;
		public double DefenderTotalPopulation;
		public int AttackerBaseDamage;
		public int AttackerBaseDurability;
		public int DefenderBaseDamage;
		public int DefenderBaseDurability;

		public static IReadOnlyList<WarScenarioSpec> All() {
			const double equalPop = 10_000_000;
			const double strongPop = 20_000_000;
			const double weakPop = 5_000_000;
			const int equalSkill = 40;
			const int strongSkill = 80;

			return new List<WarScenarioSpec> {
				new WarScenarioSpec {
					Name = "1. Equal opponents",
					AttackerTotalPopulation = equalPop,
					DefenderTotalPopulation = equalPop,
					AttackerBaseDamage = equalSkill,
					AttackerBaseDurability = equalSkill,
					DefenderBaseDamage = equalSkill,
					DefenderBaseDurability = equalSkill
				},
				new WarScenarioSpec {
					Name = "2. Strong attacker (recruits count)",
					AttackerTotalPopulation = strongPop,
					DefenderTotalPopulation = weakPop,
					AttackerBaseDamage = equalSkill,
					AttackerBaseDurability = equalSkill,
					DefenderBaseDamage = equalSkill,
					DefenderBaseDurability = equalSkill
				},
				new WarScenarioSpec {
					Name = "3. Strong defender (recruits count)",
					AttackerTotalPopulation = weakPop,
					DefenderTotalPopulation = strongPop,
					AttackerBaseDamage = equalSkill,
					AttackerBaseDurability = equalSkill,
					DefenderBaseDamage = equalSkill,
					DefenderBaseDurability = equalSkill
				},
				new WarScenarioSpec {
					Name = "4. Strong attacker (damage skill)",
					AttackerTotalPopulation = equalPop,
					DefenderTotalPopulation = equalPop,
					AttackerBaseDamage = strongSkill,
					AttackerBaseDurability = equalSkill,
					DefenderBaseDamage = equalSkill,
					DefenderBaseDurability = equalSkill
				},
				new WarScenarioSpec {
					Name = "5. Strong defender (damage skill)",
					AttackerTotalPopulation = equalPop,
					DefenderTotalPopulation = equalPop,
					AttackerBaseDamage = equalSkill,
					AttackerBaseDurability = equalSkill,
					DefenderBaseDamage = strongSkill,
					DefenderBaseDurability = equalSkill
				},
				new WarScenarioSpec {
					Name = "6. Strong attacker (durability skill)",
					AttackerTotalPopulation = equalPop,
					DefenderTotalPopulation = equalPop,
					AttackerBaseDamage = equalSkill,
					AttackerBaseDurability = strongSkill,
					DefenderBaseDamage = equalSkill,
					DefenderBaseDurability = equalSkill
				},
				new WarScenarioSpec {
					Name = "7. Strong defender (durability skill)",
					AttackerTotalPopulation = equalPop,
					DefenderTotalPopulation = equalPop,
					AttackerBaseDamage = equalSkill,
					AttackerBaseDurability = equalSkill,
					DefenderBaseDamage = equalSkill,
					DefenderBaseDurability = strongSkill
				}
			};
		}
	}
}
