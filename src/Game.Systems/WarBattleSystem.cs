using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

	namespace GS.Game.Systems {
	public static partial class WarBattleSystem {
		readonly struct WarInfo {
			public readonly int Entity;
			public readonly string WarId;
			public readonly int Capacity;

			public WarInfo(int entity, string warId, int capacity) {
				Entity = entity;
				WarId = warId;
				Capacity = capacity;
			}
		}

		public static void Update(
			World world, DateTime previousTime, DateTime currentTime, Random rng,
			ProvinceTopology topology, WarBattleSettings settings) {
			settings.Validate();
			List<WarInfo> wars = GetWars(world);
			if (wars.Count == 0) {
				return;
			}

			long intervalTicks = checked((long)Math.Round(
				TimeSpan.TicksPerHour * settings.RoundIntervalHours,
				MidpointRounding.AwayFromZero));
			long previousBucket = previousTime.Ticks / intervalTicks;
			long currentBucket = currentTime.Ticks / intervalTicks;
			bool processedBoundary = false;
			for (long bucket = previousBucket + 1; bucket <= currentBucket; bucket++) {
				processedBoundary = true;
				foreach (WarInfo war in wars) {
					FillSlots(world, war, rng, topology, settings);
					List<WarBattles.BattleInfo> active =
						WarBattles.GetBattles(world, war.WarId, BattleState.Active);
					foreach (WarBattles.BattleInfo battle in active) {
						ProcessRound(world, war, battle, rng, settings);
					}
				}
			}

			if (!processedBoundary) {
				foreach (WarInfo war in wars) {
					FillSlots(world, war, rng, topology, settings);
				}
			}
		}

		static List<WarInfo> GetWars(IReadOnlyWorld world) {
			var result = new List<WarInfo>();
			int[] required = { TypeId<War>.Value, TypeId<WarBattleCapacity>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				War[] wars = arch.GetColumn<War>();
				WarBattleCapacity[] capacities = arch.GetColumn<WarBattleCapacity>();
				for (int i = 0; i < arch.Count; i++) {
					result.Add(new WarInfo(
						arch.Entities[i], wars[i].WarId, capacities[i].MaxConcurrentBattleCount));
				}
			}
			result.Sort((a, b) => string.CompareOrdinal(a.WarId, b.WarId));
			return result;
		}
	}
}
