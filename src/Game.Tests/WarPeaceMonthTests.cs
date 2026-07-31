using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WarPeaceMonthTests {
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1 = new DateTime(1880, 2, 1, 0, 0, 0);
		static readonly DateTime Jan1 = new DateTime(1880, 1, 1, 0, 0, 0);
		static readonly DateTime Jan2 = new DateTime(1880, 1, 2, 0, 0, 0);

		static GameSettings DefaultSettings() => new GameSettings {
			PeaceMinLoseBand = 20,
			PeaceMinWinBand = 20,
			PeaceChanceMinPercent = 1,
			PeaceChanceMaxPercent = 100,
			AttackerWarProgressDecayPerMonth = 2.5,
		};

		static Dictionary<string, (double Lon, double Lat)> EmptyCenters() =>
			new Dictionary<string, (double Lon, double Lat)>();

		static int SeedWar(World world, string warId, string attacker, string defender, double progress, DateTime declaredAt) {
			int warEntity = world.Create();
			world.Add(warEntity, new War { WarId = warId, DeclaredAt = declaredAt });

			int progressEntity = world.Create();
			world.Add(progressEntity, new ResourceOwner(warId, OwnerType.War));
			world.Add(progressEntity, new Resource { ResourceId = ResourceDefinitions.WarProgress, Value = progress });

			int attackerEntity = world.Create();
			world.Add(attackerEntity, new WarParticipant {
				WarId = warId,
				Kind = WarParticipantKind.Attacker,
				CountryId = attacker
			});
			int defenderEntity = world.Create();
			world.Add(defenderEntity, new WarParticipant {
				WarId = warId,
				Kind = WarParticipantKind.Defender,
				CountryId = defender
			});
			return warEntity;
		}

		static int CountWars(World world) {
			int count = 0;
			int[] req = { TypeId<War>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				count += arch.Count;
			}
			return count;
		}

		[Fact]
		void successful_roll_on_month_boundary_destroys_war_before_decay() {
			var world = new World();
			int warEntity = SeedWar(world, "war_1", "A", "B", 100, Jan1);
			var settings = DefaultSettings();

			// Progress 100 → 100% chance; any NextDouble() succeeds
			Wars.TryResolvePeaceByChance(world, Jan31, Feb1, new Random(1), settings, EmptyCenters(), 100);
			WarSystem.Update(world, Jan31, Feb1, settings.AttackerWarProgressDecayPerMonth);

			Assert.Equal(0, CountWars(world));
			Assert.False(world.IsAlive(warEntity));
		}

		[Fact]
		void failed_roll_leaves_war_then_decay_applies() {
			var world = new World();
			int warEntity = SeedWar(world, "war_1", "A", "B", 80, Jan1); // edge → 1% chance
			var settings = DefaultSettings();

			// Seed chosen so first NextDouble() is >= 0.01 (roll fails)
			var rng = new Random(0);
			Wars.TryResolvePeaceByChance(world, Jan31, Feb1, rng, settings, EmptyCenters(), 100);
			WarSystem.Update(world, Jan31, Feb1, settings.AttackerWarProgressDecayPerMonth);

			Assert.Equal(1, CountWars(world));
			Assert.True(world.IsAlive(warEntity));
			Assert.Equal(80 - 2.5, ResourceQuery.GetValue(world, "war_1", ResourceDefinitions.WarProgress));
		}

		[Fact]
		void non_boundary_tick_does_nothing_for_chance_or_decay() {
			var world = new World();
			int warEntity = SeedWar(world, "war_1", "A", "B", 100, Jan1);
			var settings = DefaultSettings();

			Wars.TryResolvePeaceByChance(world, Jan1, Jan2, new Random(1), settings, EmptyCenters(), 100);
			WarSystem.Update(world, Jan1, Jan2, settings.AttackerWarProgressDecayPerMonth);

			Assert.Equal(1, CountWars(world));
			Assert.Equal(100, ResourceQuery.GetValue(world, "war_1", ResourceDefinitions.WarProgress));
		}

		[Fact]
		void progress_outside_bands_skips_roll_and_still_decays() {
			var world = new World();
			int warEntity = SeedWar(world, "war_1", "A", "B", 0, Jan1);
			var settings = DefaultSettings();

			Wars.TryResolvePeaceByChance(world, Jan31, Feb1, new Random(1), settings, EmptyCenters(), 100);
			WarSystem.Update(world, Jan31, Feb1, settings.AttackerWarProgressDecayPerMonth);

			Assert.Equal(1, CountWars(world));
			Assert.Equal(-2.5, ResourceQuery.GetValue(world, "war_1", ResourceDefinitions.WarProgress));
		}
	}
}
