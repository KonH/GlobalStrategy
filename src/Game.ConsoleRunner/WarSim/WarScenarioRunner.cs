using System;
using System.Collections.Generic;
using System.Linq;
using ECS;
using GS.Configs;
using GS.Configs.IO;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;

namespace GS.Game.ConsoleRunner.WarSim {
	// Runs a single headless war (declare -> tick daily -> peace resolution or timeout) against an
	// in-memory config, with no Unity/file dependency. Mirrors the GameLogicWarBattleTests pattern
	// (src/Game.Tests/GameLogicWarBattleTests.cs) but drives the war to completion instead of a
	// single tick, so it can report war-level metrics (duration, battle count, losses, winner).
	public static class WarScenarioRunner {
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		// Real countries and their real province graphs (Assets/Configs/province_config.json), used
		// as-is instead of a synthetic map — a flat 1:1 border (every province touching the enemy)
		// let the whole loser's territory be occupied turn one, which isn't how actual multi-province
		// countries with deep interiors are laid out. France (65 provinces) and Germany (50) share a
		// real border (3 cross-border neighbor pairs), so battles mostly use
		// WarBattleFill.SelectTarget's adjacency-first ("primary") path rather than the
		// nearest-N-by-centroid-distance fallback — a genuine front line, not just real geography.
		public const string AttackerCountryId = "France";
		public const string DefenderCountryId = "Germany";
		const string OrgId = "Illuminati";
		const string RealProvinceConfigPath = "Assets/Configs/province_config.json";
		const double HoursPerTick = 24;
		// Must match GameSettings.RecruitsInitialPercent in BuildSettings below.
		const double RecruitsInitialPercent = 5;

		static ProvinceConfig? _realProvinceConfigCache;

		public static WarScenarioResult RunOnce(
			WarScenarioSpec spec, int seed, int maxDays, Action<GameSettings>? configureSettings = null) {
			ProvinceConfig provinceConfig = BuildProvinces(spec);
			GameSettings settings = BuildSettings();
			configureSettings?.Invoke(settings);
			var context = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(BuildCountries(spec)),
				new StaticConfig<GameSettings>(settings),
				new StaticConfig<ResourceConfig>(BuildResources()),
				new StaticConfig<OrganizationConfig>(BuildOrganizations()),
				initialOrganizationId: OrgId,
				province: new StaticConfig<ProvinceConfig>(provinceConfig),
				rngSeed: seed);

			var logic = new GameLogic(context);
			logic.Update(0);

			logic.Commands.Push(new DebugDeclareWarCommand {
				AttackerCountryId = AttackerCountryId,
				DefenderCountryId = DefenderCountryId
			});

			var attackerProvinceIds = new List<string>();
			var defenderProvinceIds = new List<string>();
			foreach (var entry in provinceConfig.Provinces) {
				(entry.CountryId == AttackerCountryId ? attackerProvinceIds : defenderProvinceIds).Add(entry.ProvinceId);
			}

			double attackerInitialRecruits = spec.AttackerTotalPopulation * RecruitsInitialPercent / 100.0;
			double defenderInitialRecruits = spec.DefenderTotalPopulation * RecruitsInitialPercent / 100.0;

			int attackerOccupiedPeak = 0;
			int defenderOccupiedPeak = 0;
			int daysElapsed = 0;
			for (int day = 0; day < maxDays; day++) {
				attackerOccupiedPeak = Math.Max(attackerOccupiedPeak, CountOccupied(logic.World, attackerProvinceIds));
				defenderOccupiedPeak = Math.Max(defenderOccupiedPeak, CountOccupied(logic.World, defenderProvinceIds));

				logic.Update((float)HoursPerTick);
				daysElapsed = day + 1;

				if (TryGetResolvedWar(logic.World, out WarResolvedApplied applied)) {
					bool attackerWon = applied.WinnerCountryId == AttackerCountryId;
					int loserOccupiedPeak = attackerWon ? defenderOccupiedPeak : attackerOccupiedPeak;
					string? firstBattleWinnerCountryId = GetFirstBattleWinnerCountryId(applied.Battles);
					return new WarScenarioResult {
						Resolved = true,
						DurationDays = daysElapsed,
						BattleCount = applied.Battles.Count,
						AttackerProvincesOccupiedPeak = attackerOccupiedPeak,
						DefenderProvincesOccupiedPeak = defenderOccupiedPeak,
						AttackerLosses = applied.Attacker.Casualties,
						DefenderLosses = applied.Defender.Casualties,
						AttackerLossPercent = 100.0 * applied.Attacker.Casualties / attackerInitialRecruits,
						DefenderLossPercent = 100.0 * applied.Defender.Casualties / defenderInitialRecruits,
						LoserProvincesOccupiedPercent = 100.0 * loserOccupiedPeak /
							(attackerWon ? defenderProvinceIds.Count : attackerProvinceIds.Count),
						Winner = attackerWon ? "Attacker" : "Defender",
						FinalProgress = applied.Progress,
						FirstBattleWinnerWonWar = firstBattleWinnerCountryId == null
							? (bool?)null
							: firstBattleWinnerCountryId == applied.WinnerCountryId
					};
				}
			}

			// Timed out before peace resolved: report a best-effort snapshot of the still-active war.
			string? warId = FindWarId(logic.World, AttackerCountryId);
			if (warId == null) {
				return new WarScenarioResult { Resolved = false, DurationDays = daysElapsed, Winner = "Unresolved" };
			}
			double progress = logic.Resources.GetValue(logic.World, warId, ResourceDefinitions.WarProgress);
			WarSideStatsSnapshot attackerStats = WarProgressSnapshot.BuildSideStats(
				logic.World, logic.Resources, warId, AttackerCountryId, WarParticipantKind.Attacker, logic.CountryConfig);
			WarSideStatsSnapshot defenderStats = WarProgressSnapshot.BuildSideStats(
				logic.World, logic.Resources, warId, DefenderCountryId, WarParticipantKind.Defender, logic.CountryConfig);
			int battleCount = WarProgressSnapshot.BuildBattles(logic.World, warId).Count;
			// No declared winner yet — attribute "loser" occupation using whichever side progress
			// currently favors (progress > 0 favors the attacker, so the defender is the one being
			// overrun so far).
			int leadingLoserOccupiedPeak = progress >= 0 ? defenderOccupiedPeak : attackerOccupiedPeak;
			return new WarScenarioResult {
				Resolved = false,
				DurationDays = daysElapsed,
				BattleCount = battleCount,
				AttackerProvincesOccupiedPeak = attackerOccupiedPeak,
				DefenderProvincesOccupiedPeak = defenderOccupiedPeak,
				AttackerLosses = attackerStats.Casualties,
				DefenderLosses = defenderStats.Casualties,
				AttackerLossPercent = 100.0 * attackerStats.Casualties / attackerInitialRecruits,
				DefenderLossPercent = 100.0 * defenderStats.Casualties / defenderInitialRecruits,
				LoserProvincesOccupiedPercent = 100.0 * leadingLoserOccupiedPeak /
					(progress >= 0 ? defenderProvinceIds.Count : attackerProvinceIds.Count),
				Winner = "Unresolved",
				FinalProgress = progress
			};
		}

		static int CountOccupied(IReadOnlyWorld world, List<string> provinceIds) {
			int count = 0;
			foreach (string provinceId in provinceIds) {
				if (ProvinceOccupationSystem.GetOccupier(world, provinceId) != "") {
					count++;
				}
			}
			return count;
		}

		// Battle ids are "{warId}_battle_{creationSequenceIndex}" (see WarBattleFill.FillSlots), so the
		// chronologically first finished battle is the one with the smallest trailing index.
		static string? GetFirstBattleWinnerCountryId(List<WarBattleRowSnapshot> battles) {
			int firstIndex = int.MaxValue;
			string? firstWinnerCountryId = null;
			foreach (WarBattleRowSnapshot battle in battles) {
				if (!battle.IsFinished || string.IsNullOrEmpty(battle.WinnerCountryId)) {
					continue;
				}
				int index = ParseBattleIndex(battle.BattleId);
				if (index < firstIndex) {
					firstIndex = index;
					firstWinnerCountryId = battle.WinnerCountryId;
				}
			}
			return firstWinnerCountryId;
		}

		static int ParseBattleIndex(string battleId) {
			const string marker = "_battle_";
			int markerIndex = battleId.LastIndexOf(marker, StringComparison.Ordinal);
			if (markerIndex < 0) {
				return int.MaxValue;
			}
			string suffix = battleId[(markerIndex + marker.Length)..];
			return int.TryParse(suffix, out int index) ? index : int.MaxValue;
		}

		static bool TryGetResolvedWar(World world, out WarResolvedApplied value) {
			int[] required = { TypeId<WarResolvedApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					value = arch.GetColumn<WarResolvedApplied>()[0];
					return true;
				}
			}
			value = default;
			return false;
		}

		// Replicates the private Wars.FindWarIdForCountry lookup — no public equivalent exists.
		static string? FindWarId(IReadOnlyWorld world, string countryId) {
			int[] required = { TypeId<WarParticipant>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				WarParticipant[] participants = arch.GetColumn<WarParticipant>();
				for (int i = 0; i < arch.Count; i++) {
					if (participants[i].CountryId == countryId) {
						return participants[i].WarId;
					}
				}
			}
			return null;
		}

		static CountryConfig BuildCountries(WarScenarioSpec spec) {
			return new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = AttackerCountryId,
						IsAvailable = true,
						BaseDamage = spec.AttackerBaseDamage,
						BaseDurability = spec.AttackerBaseDurability
					},
					new CountryEntry {
						CountryId = DefenderCountryId,
						IsAvailable = true,
						BaseDamage = spec.DefenderBaseDamage,
						BaseDurability = spec.DefenderBaseDurability
					}
				}
			};
		}

		// Real France/Russian_Empire province ids, centroids and neighbor graphs, straight from
		// Assets/Configs/province_config.json — only Population is overridden per scenario (spread
		// evenly across that side's real province count) so the 7 cases still isolate one variable.
		// Neighbor ids pointing outside {France, Russian_Empire} are dropped since those countries
		// don't exist in this simulated world.
		static ProvinceConfig BuildProvinces(WarScenarioSpec spec) {
			ProvinceConfig real = LoadRealProvinceConfig();
			var attackerReal = real.Provinces.Where(p => p.CountryId == AttackerCountryId).ToList();
			var defenderReal = real.Provinces.Where(p => p.CountryId == DefenderCountryId).ToList();
			var idSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (var entry in attackerReal.Concat(defenderReal)) {
				idSet.Add(entry.ProvinceId);
			}

			double attackerPopulationPerProvince = spec.AttackerTotalPopulation / attackerReal.Count;
			double defenderPopulationPerProvince = spec.DefenderTotalPopulation / defenderReal.Count;

			var provinces = new List<ProvinceEntry>();
			foreach (var entry in attackerReal.Concat(defenderReal)) {
				bool isAttacker = entry.CountryId == AttackerCountryId;
				provinces.Add(new ProvinceEntry {
					ProvinceId = entry.ProvinceId,
					CountryId = entry.CountryId,
					Population = isAttacker ? attackerPopulationPerProvince : defenderPopulationPerProvince,
					CentroidX = entry.CentroidX,
					CentroidY = entry.CentroidY,
					IsMainTerritory = entry.IsMainTerritory,
					NeighborProvinceIds = entry.NeighborProvinceIds.Where(idSet.Contains).ToList()
				});
			}
			return new ProvinceConfig { Provinces = provinces };
		}

		static ProvinceConfig LoadRealProvinceConfig() {
			return _realProvinceConfigCache ??= new FileConfig<ProvinceConfig>(RealProvinceConfigPath).Load();
		}

		static ResourceConfig BuildResources() {
			return new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.Population,
						SeedTarget = ResourceSeedTarget.Province
					},
					new ResourceDefinition { ResourceId = ResourceDefinitions.CountryPopulation },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Recruits },
					new ResourceDefinition { ResourceId = ResourceDefinitions.WarInitiative },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Damage },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Durability },
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.WarProgress,
						SeedTarget = ResourceSeedTarget.War,
						RecordHistory = true,
						MinValue = -100,
						MaxValue = 100
					}
				}
			};
		}

		static OrganizationConfig BuildOrganizations() {
			return new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry { OrganizationId = OrgId, HqCountryId = AttackerCountryId }
				}
			};
		}

		// Mirrors Assets/Configs/game_settings.json's current production values (not the code
		// defaults in GameSettings.cs, which differ for battleProgressGain, roundIntervalHours,
		// peaceChanceMinPercent and the two peaceProvinceTransfer bounds) — this is the *baseline*
		// the requested table measures. MaxFinishedBattlesRetained is the one deliberate deviation:
		// raised so no battle/casualty data is pruned away before a war (potentially hundreds of
		// battles for the "equal opponents" case) resolves.
		// battleProgressGain=2 reflects idea A (see .tmp/battle_logic.md "Ideas"), applied to
		// production on 2026-08-12 after the idea-A comparison run confirmed the intended effect
		// (roughly doubles war length/battle count/occupation, losses and winner unchanged).
		// roundIntervalHours=12 reflects idea B (tried at 24h first, which crossed the one-month
		// recruit-regrowth boundary and inflated losses; 12h stretches duration +35-65% with losses
		// unchanged), applied to production the same day.
		// casualtyCoefficient=0.5 reflects idea C, applied to production the same day despite mixed
		// results (helped the recruits-asymmetry cases, but flipped the winner in both damage-skill
		// cases and raised both sides' losses in the equal-opponents case) — accepted anyway.
		// casualtyRandomMin/Max=0.8/1.2 reflects idea G (widening the existing casualty randomization
		// range from 0.2 to 0.4), applied to production despite the comparison run showing only
		// noise-level effect on every metric — accepted anyway.
		static GameSettings BuildSettings() {
			return new GameSettings {
				SpeedMultipliers = new[] { 1 },
				ResourceIdUpdateOrder = new[] {
					ResourceDefinitions.Population,
					ResourceDefinitions.CountryPopulation,
					ResourceDefinitions.Recruits,
					ResourceDefinitions.Damage,
					ResourceDefinitions.Durability
				},
				RecruitsInitialPercent = 5,
				RecruitsCapPercent = 15,
				RecruitsMonthlyIncreasePercent = 1,
				AttackerWarProgressDecayPerMonth = 2.5,
				RevengeDamageBonusDecayPerMonth = 1.0,
				RevengeDurabilityBonusDecayPerMonth = 0.5,
				PeaceMinLoseBand = 20,
				PeaceMinWinBand = 20,
				PeaceChanceMinPercent = 0.3,
				PeaceChanceMaxPercent = 100,
				PeaceProvinceTransferMinPercent = 50,
				PeaceProvinceTransferMaxPercent = 80,
				PeaceGoldPerMonth = 1000,
				PeaceWinnerControlIncreaseFraction = 0.5,
				PeaceLoserControlDecreaseFraction = 1.0,
				WarBattles = new WarBattleSettings {
					BaseConcurrentBattleCount = 1,
					SharedBorderPairsPerAdditionalBattle = 5,
					AttackerInitialInitiative = 1.0,
					DefenderInitialInitiative = 0.0,
					InitiationCost = 1.0,
					RoundWinnerInitiativeGain = 0.5,
					BattleProgressGain = 2,
					FallbackCandidateCount = 3,
					TroopDenominatorOffset = 1,
					TroopRandomMin = 0.9,
					TroopRandomMax = 1.1,
					RoundIntervalHours = 12,
					DamageDivisor = 300,
					DurabilityDivisor = 300,
					CasualtyRandomMin = 0.8,
					CasualtyRandomMax = 1.2,
					MinimumCasualtyFraction = 0.01,
					MinimumAbsoluteCasualties = 1,
					CasualtyCoefficient = 0.5,
					ReservePercent = 0,
					MaxFinishedBattlesRetained = 1_000_000
				}
			};
		}
	}
}
