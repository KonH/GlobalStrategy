using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class PeaceResolutionTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static readonly DateTime DeclareTime = new DateTime(1880, 1, 1);
		static readonly DateTime PeaceTime = new DateTime(1880, 4, 1);
		// Exactly three 30-day billable months under ceil(days/30).
		static readonly DateTime ThreeMonthPeaceTime = DeclareTime.AddDays(90);

		static GameSettings DefaultSettings() => new GameSettings {
			PeaceProvinceTransferMinPercent = 10,
			PeaceProvinceTransferMaxPercent = 30,
			PeaceGoldPerMonth = 100,
			PeaceWinnerControlIncreaseFraction = 0.05,
			PeaceLoserControlDecreaseFraction = 0.10,
		};

		static Dictionary<string, (double Lon, double Lat)> EmptyCenters() =>
			new Dictionary<string, (double Lon, double Lat)>();

		static ProvinceTopology EmptyTopology() => new ProvinceTopology(new ProvinceConfig());

		void SetProgress(World world, string warId, double value) {
			ResourceMutations.TrySetValue(_resources, world, warId, ResourceDefinitions.WarProgress, value, out _);
		}

		static string GetOnlyWarId(World world) {
			int[] required = { TypeId<War>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				War[] wars = arch.GetColumn<War>();
				if (arch.Count > 0) {
					return wars[0].WarId;
				}
			}
			throw new InvalidOperationException("no war");
		}

		static void AddOwnership(World world, string provinceId, string ownerId) {
			int e = world.Create();
			world.Add(e, new ProvinceOwnership { ProvinceId = provinceId, OwnerId = ownerId });
		}

		static void AddOccupation(World world, string provinceId, string occupierId) {
			int e = world.Create();
			world.Add(e, new ProvinceOccupation { ProvinceId = provinceId, OccupierId = occupierId });
		}

		static void AddControl(World world, string orgId, string countryId, int value, string? effectId = null) {
			int e = world.Create();
			world.Add(e, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = effectId ?? $"base_{orgId}"
			});
		}

		static int AddGold(World world, string ownerId, OwnerType ownerType, double value) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(ownerId, ownerType));
			world.Add(e, new Resource { ResourceId = ResourceDefinitions.Gold, Value = value });
			return e;
		}

		static double GetGold(World world, string ownerId, OwnerType ownerType) {
			int[] req = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == ownerId
						&& owners[i].OwnerType == ownerType
						&& resources[i].ResourceId == ResourceDefinitions.Gold) {
						return resources[i].Value;
					}
				}
			}
			return 0;
		}

		static int CountEntities<T>(World world) {
			int count = 0;
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				count += arch.Count;
			}
			return count;
		}

		[Fact]
		void positive_progress_makes_attacker_winner() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Attacker", "Defender", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddOwnership(world, "p_lose", "Defender");
			AddOccupation(world, "p_lose", "Attacker");
			AddOwnership(world, "p_win", "Attacker");
			AddOccupation(world, "p_win", "");

			var centers = new Dictionary<string, (double Lon, double Lat)> {
				["p_lose"] = (0, 0),
				["p_win"] = (0, 0),
			};
			var settings = DefaultSettings();
			settings.PeaceProvinceTransferMinPercent = 100;
			settings.PeaceProvinceTransferMaxPercent = 100;

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), settings, EmptyTopology(), centers, 100);

			Assert.Equal("Attacker", ProvinceOwnershipSystem.GetOwner(world, "p_lose"));
			Assert.Equal(0, CountEntities<War>(world));
		}

		[Fact]
		void negative_progress_makes_defender_winner() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Attacker", "Defender", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, -50);

			AddOwnership(world, "p_lose", "Attacker");
			AddOccupation(world, "p_lose", "Defender");
			AddOwnership(world, "p_win", "Defender");
			AddOccupation(world, "p_win", "");

			var centers = new Dictionary<string, (double Lon, double Lat)> {
				["p_lose"] = (0, 0),
				["p_win"] = (0, 0),
			};
			var settings = DefaultSettings();
			settings.PeaceProvinceTransferMinPercent = 100;
			settings.PeaceProvinceTransferMaxPercent = 100;

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), settings, EmptyTopology(), centers, 100);

			Assert.Equal("Defender", ProvinceOwnershipSystem.GetOwner(world, "p_lose"));
			Assert.Equal(0, CountEntities<War>(world));
		}

		[Fact]
		void transfer_prefers_provinces_closer_to_winner_centroid_with_ceiling() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 80);

			// Winner centroid at (0,0)
			AddOwnership(world, "w1", "Winner");
			AddOccupation(world, "w1", "");

			// 3 eligible loser provinces; 30% → Ceiling(0.9)=1 → closest only
			AddOwnership(world, "near", "Loser");
			AddOccupation(world, "near", "Winner");
			AddOwnership(world, "mid", "Loser");
			AddOccupation(world, "mid", "Third");
			AddOwnership(world, "far", "Loser");
			AddOccupation(world, "far", "Winner");

			var centers = new Dictionary<string, (double Lon, double Lat)> {
				["w1"] = (0, 0),
				["near"] = (1, 0),
				["mid"] = (5, 0),
				["far"] = (10, 0),
			};
			var settings = DefaultSettings();
			settings.PeaceProvinceTransferMinPercent = 30;
			settings.PeaceProvinceTransferMaxPercent = 30;

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), settings, EmptyTopology(), centers, 100);

			Assert.Equal("Winner", ProvinceOwnershipSystem.GetOwner(world, "near"));
			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "mid"));
			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "far"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "near"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "mid"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "far"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "w1"));
		}

		[Fact]
		void transfer_prefers_province_near_winner_mainland_over_disconnected_colony() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 80);

			// Winner mainland: two provinces around x=0-2, flagged isMainTerritory. Winner colony:
			// a province far away at x=100, generated from a secondaryMapFeatureId (isMainTerritory
			// false). Averaging over every owned province (mainland + colony) would pull the centroid
			// to x=34, which is much closer to "near_colony" than to "near_mainland" — the transfer
			// should still prefer "near_mainland".
			var topology = new ProvinceTopology(new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "home1", CentroidX = 0, CentroidY = 0, IsMainTerritory = true },
					new ProvinceEntry { ProvinceId = "home2", CentroidX = 2, CentroidY = 0, IsMainTerritory = true },
					new ProvinceEntry { ProvinceId = "colony", CentroidX = 100, CentroidY = 0, IsMainTerritory = false },
				}
			});
			AddOwnership(world, "home1", "Winner");
			AddOccupation(world, "home1", "");
			AddOwnership(world, "home2", "Winner");
			AddOccupation(world, "home2", "");
			AddOwnership(world, "colony", "Winner");
			AddOccupation(world, "colony", "");

			AddOwnership(world, "near_mainland", "Loser");
			AddOccupation(world, "near_mainland", "Winner");
			AddOwnership(world, "near_colony", "Loser");
			AddOccupation(world, "near_colony", "Winner");

			var centers = new Dictionary<string, (double Lon, double Lat)> {
				["home1"] = (0, 0),
				["home2"] = (2, 0),
				["colony"] = (100, 0),
				["near_mainland"] = (3, 0),
				["near_colony"] = (33, 0),
			};
			var settings = DefaultSettings();
			settings.PeaceProvinceTransferMinPercent = 50;
			settings.PeaceProvinceTransferMaxPercent = 50;

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), settings, topology, centers, 100);

			Assert.Equal("Winner", ProvinceOwnershipSystem.GetOwner(world, "near_mainland"));
			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "near_colony"));
		}

		[Fact]
		void centroid_falls_back_to_every_owned_province_when_winner_holds_no_main_territory() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 80);

			// Winner holds only colonial provinces (no isMainTerritory=true holdings) — centroid
			// computation should fall back to averaging every owned province instead of yielding
			// no centroid at all.
			var topology = new ProvinceTopology(new ProvinceConfig {
				Provinces = new List<ProvinceEntry> {
					new ProvinceEntry { ProvinceId = "colony", CentroidX = 0, CentroidY = 0, IsMainTerritory = false },
				}
			});
			AddOwnership(world, "colony", "Winner");
			AddOccupation(world, "colony", "");
			AddOwnership(world, "near", "Loser");
			AddOccupation(world, "near", "Winner");

			var centers = new Dictionary<string, (double Lon, double Lat)> {
				["colony"] = (0, 0),
				["near"] = (1, 0),
			};
			var settings = DefaultSettings();
			settings.PeaceProvinceTransferMinPercent = 100;
			settings.PeaceProvinceTransferMaxPercent = 100;

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), settings, topology, centers, 100);

			Assert.Equal("Winner", ProvinceOwnershipSystem.GetOwner(world, "near"));
		}

		[Fact]
		void zero_eligible_skips_ownership_change_but_clears_occupation() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 80);

			AddOwnership(world, "p1", "Loser");
			AddOccupation(world, "p1", ""); // unoccupied — not eligible
			AddOwnership(world, "p2", "Winner");
			AddOccupation(world, "p2", "Loser");

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "p1"));
			Assert.Equal("Winner", ProvinceOwnershipSystem.GetOwner(world, "p2"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "p1"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "p2"));
		}

		[Fact]
		void gold_spoils_use_duration_org_proportions_country_remainder_and_debt() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			// D = ceil(90/30) = 3 months × 100 = 300
			AddControl(world, "OrgLoseA", "Loser", 30);
			AddControl(world, "OrgLoseB", "Loser", 70);
			AddGold(world, "OrgLoseA", OwnerType.Org, 10); // will go into debt
			AddGold(world, "OrgLoseB", OwnerType.Org, 500);

			AddControl(world, "OrgWin", "Winner", 40);
			AddGold(world, "OrgWin", OwnerType.Org, 0);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, _resources, warId, ThreeMonthPeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			// Loser: shares vs pool 100 — OrgLoseA 30%, OrgLoseB 70%
			Assert.Equal(10 - 90, GetGold(world, "OrgLoseA", OwnerType.Org), precision: 6);
			Assert.Equal(500 - 210, GetGold(world, "OrgLoseB", OwnerType.Org), precision: 6);
			Assert.Equal(0, GetGold(world, "Loser", OwnerType.Country), precision: 6);

			// Winner: OrgWin 40/100 → 40% = 120, country remainder 180
			Assert.Equal(120, GetGold(world, "OrgWin", OwnerType.Org), precision: 6);
			Assert.Equal(180, GetGold(world, "Winner", OwnerType.Country), precision: 6);
		}

		[Fact]
		void gold_org_share_uses_control_pool_not_sum_of_org_control() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddGold(world, "Loser", OwnerType.Country, 5000);
			AddControl(world, "OrgWin", "Winner", 10);
			AddGold(world, "OrgWin", OwnerType.Org, 0);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, _resources, warId, DeclareTime.AddDays(30), new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			// 1 month × 100 = 100; org 10/100 → 10, country 90
			Assert.Equal(10, GetGold(world, "OrgWin", OwnerType.Org), precision: 6);
			Assert.Equal(90, GetGold(world, "Winner", OwnerType.Country), precision: 6);
		}

		[Fact]
		void gold_with_no_controlling_orgs_goes_entirely_to_country() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddGold(world, "Loser", OwnerType.Country, 1000);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, _resources, warId, ThreeMonthPeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Equal(700, GetGold(world, "Loser", OwnerType.Country), precision: 6);
			Assert.Equal(300, GetGold(world, "Winner", OwnerType.Country), precision: 6);
		}

		[Fact]
		void zero_day_peace_transfers_no_gold() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddGold(world, "Loser", OwnerType.Country, 1000);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, _resources, warId, DeclareTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Equal(1000, GetGold(world, "Loser", OwnerType.Country));
			Assert.Equal(0, GetGold(world, "Winner", OwnerType.Country));
		}

		[Fact]
		void billable_war_months_ceil_partial_thirty_day_periods() {
			Assert.Equal(0, Wars.ComputeBillableWarMonths(DeclareTime, DeclareTime));
			Assert.Equal(1, Wars.ComputeBillableWarMonths(DeclareTime, DeclareTime.AddDays(2)));
			Assert.Equal(1, Wars.ComputeBillableWarMonths(DeclareTime, DeclareTime.AddDays(30)));
			Assert.Equal(2, Wars.ComputeBillableWarMonths(DeclareTime, DeclareTime.AddDays(32)));
			Assert.Equal(3, Wars.ComputeBillableWarMonths(DeclareTime, DeclareTime.AddDays(90)));
		}

		[Fact]
		void short_war_of_two_days_bills_one_month_of_gold() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddGold(world, "Loser", OwnerType.Country, 5000);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, _resources, warId, DeclareTime.AddDays(2), new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Equal(4900, GetGold(world, "Loser", OwnerType.Country), precision: 6);
			Assert.Equal(100, GetGold(world, "Winner", OwnerType.Country), precision: 6);
		}

		[Fact]
		void control_shifts_top_first_with_fractions() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddControl(world, "OrgWinTop", "Winner", 40);
			AddControl(world, "OrgWinLow", "Winner", 20);
			AddControl(world, "OrgLoseTop", "Loser", 40);
			AddControl(world, "OrgLoseLow", "Loser", 20);

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Equal(42, ControlQuery.GetOrgControlInCountry(world, "OrgWinTop", "Winner"));
			Assert.Equal(21, ControlQuery.GetOrgControlInCountry(world, "OrgWinLow", "Winner"));
			Assert.Equal(36, ControlQuery.GetOrgControlInCountry(world, "OrgLoseTop", "Loser"));
			Assert.Equal(18, ControlQuery.GetOrgControlInCountry(world, "OrgLoseLow", "Loser"));
		}

		[Fact]
		void winner_boost_with_base_effect_near_full_pool_stays_at_or_below_max() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			// base_* only: ApplyChangeControl alone would create permanent without counting base → overflow
			AddControl(world, "OrgWin", "Winner", 96, "base_OrgWin");
			AddControl(world, "OrgLose", "Loser", 10, "base_OrgLose");

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.True(ControlQuery.GetTotalControlInCountry(world, "Winner") <= 100);
			// desired = Round(96 * 0.05) = 5, room = 4 → +4 → total 100
			Assert.Equal(100, ControlQuery.GetTotalControlInCountry(world, "Winner"));
			Assert.Equal(100, ControlQuery.GetOrgControlInCountry(world, "OrgWin", "Winner"));
		}

		[Fact]
		void progress_zero_stop_war_skips_transfer_gold_and_control() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "A", "B", DeclareTime);
			// progress stays 0

			AddOwnership(world, "p_b", "B");
			AddOccupation(world, "p_b", "A");
			AddOwnership(world, "p_a", "A");
			AddOccupation(world, "p_a", "");

			AddControl(world, "OrgA", "A", 40);
			AddControl(world, "OrgB", "B", 40);
			AddGold(world, "A", OwnerType.Country, 500);
			AddGold(world, "B", OwnerType.Country, 500);

			bool result = Wars.StopWar(world, _resources, "A", PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.True(result);
			Assert.Equal(0, CountEntities<War>(world));
			Assert.Equal("B", ProvinceOwnershipSystem.GetOwner(world, "p_b"));
			Assert.Equal("A", ProvinceOwnershipSystem.GetOwner(world, "p_a"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "p_b"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "p_a"));
			Assert.Equal(40, ControlQuery.GetOrgControlInCountry(world, "OrgA", "A"));
			Assert.Equal(40, ControlQuery.GetOrgControlInCountry(world, "OrgB", "B"));
			Assert.Equal(500, GetGold(world, "A", OwnerType.Country));
			Assert.Equal(500, GetGold(world, "B", OwnerType.Country));
		}

		[Fact]
		void peace_resolution_does_not_touch_country_relations() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "A", "B", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);
			_relations.SetRelation(world, "A", "B", RelationKind.Rival);

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Equal(RelationKind.Rival, _relations.GetRelation(world, "A", "B"));
		}

		[Fact]
		void resolve_peace_creates_war_resolved_log_event_with_winner_and_loser() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Attacker", "Defender", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			WarResolvedApplied applied = Assert.Single(GetComponents<WarResolvedApplied>(world));
			Assert.Equal("Attacker", applied.WinnerCountryId);
			Assert.Equal("Defender", applied.LoserCountryId);
		}

		[Fact]
		void resolve_peace_emits_enriched_war_resolved_snapshot() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Attacker", "Defender", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddOwnership(world, "p_lose", "Defender");
			AddOccupation(world, "p_lose", "Attacker");
			AddOwnership(world, "p_win", "Attacker");
			AddOccupation(world, "p_win", "");

			AddControl(world, "OrgWin", "Attacker", 40);
			AddControl(world, "OrgLose", "Defender", 40);
			AddGold(world, "OrgWin", OwnerType.Org, 0);
			AddGold(world, "OrgLose", OwnerType.Org, 500);
			AddGold(world, "Defender", OwnerType.Country, 0);

			int[] historyRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value,
				TypeId<ResourceHistory>.Value
			};
			foreach (var arch in world.GetMatchingArchetypes(historyRequired, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == warId && resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						arch.GetColumn<ResourceHistory>()[i].History = new List<ResourceChangeEntry> {
							new ResourceChangeEntry {
								EffectId = "battle_win",
								AppliedDelta = 50,
								Timestamp = DeclareTime
							}
						};
						break;
					}
				}
			}

			var centers = new Dictionary<string, (double Lon, double Lat)> {
				["p_lose"] = (0, 0),
				["p_win"] = (0, 0),
			};
			var settings = DefaultSettings();
			settings.PeaceProvinceTransferMinPercent = 100;
			settings.PeaceProvinceTransferMaxPercent = 100;

			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Attacker", BaseDamage = 55, BaseDurability = 66 },
					new CountryEntry { CountryId = "Defender", BaseDamage = 33, BaseDurability = 44 },
				}
			};

			Wars.ResolvePeace(world, _resources, warId, ThreeMonthPeaceTime, new Random(1), settings, EmptyTopology(), centers, 100, countryConfig);

			WarResolvedApplied applied = Assert.Single(GetComponents<WarResolvedApplied>(world));
			Assert.Equal(warId, applied.WarId);
			Assert.Equal("Attacker", applied.AttackerCountryId);
			Assert.Equal("Defender", applied.DefenderCountryId);
			Assert.Equal("Attacker", applied.WinnerCountryId);
			Assert.Equal("Defender", applied.LoserCountryId);
			Assert.Equal(50, applied.Progress);
			Assert.Equal(300, applied.GoldTaken, precision: 6);
			Assert.NotNull(applied.GoldRecipients);
			Assert.Contains(applied.GoldRecipients, r =>
				r.OwnerType == OwnerType.Org && r.OwnerId == "OrgWin" && Math.Abs(r.Amount - 120) < 1e-6);
			Assert.Contains(applied.GoldRecipients, r =>
				r.OwnerType == OwnerType.Country && r.OwnerId == "Attacker" && Math.Abs(r.Amount - 180) < 1e-6);
			Assert.NotNull(applied.ControlDeltas);
			Assert.Contains(applied.ControlDeltas, d =>
				d.CountryId == "Attacker" && d.OrgId == "OrgWin" && d.Delta > 0 && d.TotalAfter == 42);
			Assert.Contains(applied.ControlDeltas, d =>
				d.CountryId == "Defender" && d.OrgId == "OrgLose" && d.Delta < 0 && d.TotalAfter == 36);
			Assert.NotNull(applied.TransferredProvinces);
			WarProvinceTransferSnapshot transfer = Assert.Single(applied.TransferredProvinces);
			Assert.Equal("p_lose", transfer.ProvinceId);
			Assert.Equal("Defender", transfer.OldOwnerCountryId);
			Assert.Equal("Attacker", transfer.NewOwnerCountryId);
			Assert.NotNull(applied.History);
			Assert.Single(applied.History);
			Assert.Equal("battle_win", applied.History[0].EffectId);
			Assert.Equal(55, applied.Attacker.DamageBase);
			Assert.Equal(66, applied.Attacker.DurabilityBase);
			Assert.Equal(33, applied.Defender.DamageBase);
			Assert.Equal(44, applied.Defender.DurabilityBase);
			Assert.NotNull(applied.Battles);
		}

		[Fact]
		void zero_day_peace_emits_zero_gold_and_empty_recipients() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			Wars.ResolvePeace(world, _resources, warId, DeclareTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			WarResolvedApplied applied = Assert.Single(GetComponents<WarResolvedApplied>(world));
			Assert.Equal(0, applied.GoldTaken);
			Assert.NotNull(applied.GoldRecipients);
			Assert.Empty(applied.GoldRecipients);
		}

		[Fact]
		void zero_eligible_provinces_emits_empty_transferred_list() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 80);

			AddOwnership(world, "p1", "Loser");
			AddOccupation(world, "p1", "");

			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			WarResolvedApplied applied = Assert.Single(GetComponents<WarResolvedApplied>(world));
			Assert.NotNull(applied.TransferredProvinces);
			Assert.Empty(applied.TransferredProvinces);
		}

		[Fact]
		void progress_zero_stop_war_creates_no_war_resolved_log_event() {
			var world = new World();
			Wars.DeclareWar(world, _resources, "A", "B", DeclareTime);
			// progress stays 0

			Wars.StopWar(world, _resources, "A", PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			Assert.Empty(GetComponents<WarResolvedApplied>(world));
		}

		[Fact]
		void war_resolved_event_produces_one_game_log_entry_with_winner_and_loser() {
			var world = new World();
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = PeaceTime });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Org", DisplayName = "Org" });
			int eventEntity = world.Create();
			world.Add(eventEntity, new WarResolvedApplied {
				WarId = "war_log",
				AttackerCountryId = "Attacker",
				DefenderCountryId = "Defender",
				WinnerCountryId = "Attacker",
				LoserCountryId = "Defender",
				Progress = 25,
				GoldTaken = 100,
				GoldRecipients = new List<WarGoldRecipientSnapshot> {
					new WarGoldRecipientSnapshot { OwnerType = OwnerType.Country, OwnerId = "Attacker", Amount = 100 }
				},
				ControlDeltas = new List<WarControlDeltaSnapshot>(),
				TransferredProvinces = new List<WarProvinceTransferSnapshot>(),
				History = new List<WarProgressHistorySnapshot>(),
				Attacker = new WarSideStatsSnapshot { CountryId = "Attacker" },
				Defender = new WarSideStatsSnapshot { CountryId = "Defender" },
				Battles = new List<WarBattleRowSnapshot>()
			});
			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations);

			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			GameLogEntry entry = Assert.Single(state.GameLog.Entries);
			Assert.Equal(GameLogEntryKind.WarResolved, entry.Kind);
			Assert.Equal("Attacker", entry.CountryId);
			Assert.Equal("Defender", entry.TargetCountryId);
			Assert.Empty(state.WarResults.Entries);

			CleanupEffectNotificationsSystem.UpdateWarResolved(world);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);
			Assert.Single(state.GameLog.Entries);
		}

		[Fact]
		void war_resolved_write_action_log_false_skips_log_entry() {
			var world = new World();
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = PeaceTime });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Org", DisplayName = "Org" });
			AddControl(world, "Org", "Attacker", 5);
			int eventEntity = world.Create();
			world.Add(eventEntity, new WarResolvedApplied {
				WarId = "war_nolog",
				AttackerCountryId = "Attacker",
				DefenderCountryId = "Defender",
				WinnerCountryId = "Attacker",
				LoserCountryId = "Defender",
				Progress = 10,
				GoldRecipients = new List<WarGoldRecipientSnapshot>(),
				ControlDeltas = new List<WarControlDeltaSnapshot>(),
				TransferredProvinces = new List<WarProvinceTransferSnapshot>(),
				History = new List<WarProgressHistorySnapshot>(),
				Attacker = new WarSideStatsSnapshot { CountryId = "Attacker" },
				Defender = new WarSideStatsSnapshot { CountryId = "Defender" },
				Battles = new List<WarBattleRowSnapshot>()
			});
			var settings = new EventNotificationSettings {
				Events = new List<EventNotificationEntry> {
					new EventNotificationEntry {
						EventType = "war_resolved",
						Pause = true,
						ShowWindow = true,
						WriteActionLog = false,
						PauseCondition = EventNotificationSettings.CreateGtControlZero(),
						ShowWindowCondition = EventNotificationSettings.CreateGtControlZero()
					}
				}
			};
			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations, eventNotifications: settings);

			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.Empty(state.GameLog.Entries);
			Assert.Single(state.WarResults.Entries);
		}

		[Fact]
		void war_resolved_enqueues_show_decisions_in_order_and_acknowledge_drains_fifo() {
			var world = new World();
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = PeaceTime });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Org", DisplayName = "Org" });
			AddControl(world, "Org", "Attacker", 5);

			int first = world.Create();
			world.Add(first, new WarResolvedApplied {
				WarId = "war_a",
				AttackerCountryId = "Attacker",
				DefenderCountryId = "Defender",
				WinnerCountryId = "Attacker",
				LoserCountryId = "Defender",
				Progress = 10,
				GoldRecipients = new List<WarGoldRecipientSnapshot>(),
				ControlDeltas = new List<WarControlDeltaSnapshot>(),
				TransferredProvinces = new List<WarProvinceTransferSnapshot>(),
				History = new List<WarProgressHistorySnapshot>(),
				Attacker = new WarSideStatsSnapshot { CountryId = "Attacker" },
				Defender = new WarSideStatsSnapshot { CountryId = "Defender" },
				Battles = new List<WarBattleRowSnapshot>()
			});
			int second = world.Create();
			world.Add(second, new WarResolvedApplied {
				WarId = "war_b",
				AttackerCountryId = "Attacker",
				DefenderCountryId = "Defender",
				WinnerCountryId = "Defender",
				LoserCountryId = "Attacker",
				Progress = -20,
				GoldRecipients = new List<WarGoldRecipientSnapshot>(),
				ControlDeltas = new List<WarControlDeltaSnapshot>(),
				TransferredProvinces = new List<WarProvinceTransferSnapshot>(),
				History = new List<WarProgressHistorySnapshot>(),
				Attacker = new WarSideStatsSnapshot { CountryId = "Attacker" },
				Defender = new WarSideStatsSnapshot { CountryId = "Defender" },
				Battles = new List<WarBattleRowSnapshot>()
			});

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.Equal(2, state.GameLog.Entries.Count);
			Assert.Equal(2, state.WarResults.Entries.Count);
			Assert.True(state.WarResults.TryPeek(out WarResultSnapshotState? peek));
			Assert.Equal("war_a", peek!.WarId);

			state.WarResults.AcknowledgeCurrent();
			Assert.True(state.WarResults.TryPeek(out peek));
			Assert.Equal("war_b", peek!.WarId);

			state.WarResults.AcknowledgeCurrent();
			Assert.False(state.WarResults.TryPeek(out _));
			Assert.Empty(state.WarResults.Entries);
		}

		[Fact]
		void action_effects_cleanup_does_not_destroy_same_tick_war_resolved_before_visual_convert() {
			var world = new World();
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = PeaceTime });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Org", DisplayName = "Org" });
			AddControl(world, "Org", "Attacker", 5);

			Wars.DeclareWar(world, _resources, "Attacker", "Defender", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 40);
			Wars.ResolvePeace(world, _resources, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyTopology(), EmptyCenters(), 100);

			// GameLogic order: peace/StopWar emit earlier, then UpdateActionEffects runs, then
			// VisualStateConverter. UpdateActionEffects must not wipe WarResolvedApplied here.
			CleanupEffectNotificationsSystem.UpdateActionEffects(world);

			Assert.Single(GetComponents<WarResolvedApplied>(world));

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			Assert.Single(state.GameLog.Entries);
			Assert.Equal(GameLogEntryKind.WarResolved, state.GameLog.Entries[0].Kind);
			Assert.Single(state.WarResults.Entries);
		}

		static List<T> GetComponents<T>(World world) where T : struct {
			var result = new List<T>();
			int[] req = { TypeId<T>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				T[] column = arch.GetColumn<T>();
				for (int i = 0; i < arch.Count; i++) {
					result.Add(column[i]);
				}
			}
			return result;
		}
	}
}
