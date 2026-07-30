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
		static readonly DateTime DeclareTime = new DateTime(1880, 1, 1);
		static readonly DateTime PeaceTime = new DateTime(1880, 4, 1);

		static GameSettings DefaultSettings() => new GameSettings {
			PeaceProvinceTransferMinPercent = 10,
			PeaceProvinceTransferMaxPercent = 30,
			PeaceGoldPerMonth = 100,
			PeaceWinnerControlIncreaseFraction = 0.05,
			PeaceLoserControlDecreaseFraction = 0.10,
		};

		static Dictionary<string, (double Lon, double Lat)> EmptyCenters() =>
			new Dictionary<string, (double Lon, double Lat)>();

		static void SetProgress(World world, string warId, double value) {
			int[] required = { TypeId<War>.Value, TypeId<WarProgress>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				War[] wars = arch.GetColumn<War>();
				WarProgress[] progresses = arch.GetColumn<WarProgress>();
				for (int i = 0; i < arch.Count; i++) {
					if (wars[i].WarId == warId) {
						progresses[i].Value = value;
						return;
					}
				}
			}
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
			Wars.DeclareWar(world, "Attacker", "Defender", DeclareTime);
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

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), settings, centers, 100);

			Assert.Equal("Attacker", ProvinceOwnershipSystem.GetOwner(world, "p_lose"));
			Assert.Equal(0, CountEntities<War>(world));
		}

		[Fact]
		void negative_progress_makes_defender_winner() {
			var world = new World();
			Wars.DeclareWar(world, "Attacker", "Defender", DeclareTime);
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

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), settings, centers, 100);

			Assert.Equal("Defender", ProvinceOwnershipSystem.GetOwner(world, "p_lose"));
			Assert.Equal(0, CountEntities<War>(world));
		}

		[Fact]
		void transfer_prefers_provinces_closer_to_winner_centroid_with_ceiling() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
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

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), settings, centers, 100);

			Assert.Equal("Winner", ProvinceOwnershipSystem.GetOwner(world, "near"));
			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "mid"));
			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "far"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "near"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "mid"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "far"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "w1"));
		}

		[Fact]
		void zero_eligible_skips_ownership_change_but_clears_occupation() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 80);

			AddOwnership(world, "p1", "Loser");
			AddOccupation(world, "p1", ""); // unoccupied — not eligible
			AddOwnership(world, "p2", "Winner");
			AddOccupation(world, "p2", "Loser");

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			Assert.Equal("Loser", ProvinceOwnershipSystem.GetOwner(world, "p1"));
			Assert.Equal("Winner", ProvinceOwnershipSystem.GetOwner(world, "p2"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "p1"));
			Assert.Equal("", ProvinceOccupationSystem.GetOccupier(world, "p2"));
		}

		[Fact]
		void gold_spoils_use_duration_org_proportions_country_remainder_and_debt() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			// D = 3 months × 100 = 300
			AddControl(world, "OrgLoseA", "Loser", 30);
			AddControl(world, "OrgLoseB", "Loser", 70);
			AddGold(world, "OrgLoseA", OwnerType.Org, 10); // will go into debt
			AddGold(world, "OrgLoseB", OwnerType.Org, 500);

			AddControl(world, "OrgWin", "Winner", 40);
			AddGold(world, "OrgWin", OwnerType.Org, 0);
			// Winner country gets remainder (no other controlling orgs cover 100% of control... OrgWin has all 40, total 40, so 100% to OrgWin)

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			// Loser: OrgLoseA pays 30%, OrgLoseB 70%
			Assert.Equal(10 - 90, GetGold(world, "OrgLoseA", OwnerType.Org), precision: 6);
			Assert.Equal(500 - 210, GetGold(world, "OrgLoseB", OwnerType.Org), precision: 6);
			Assert.Equal(0, GetGold(world, "Loser", OwnerType.Country), precision: 6);

			Assert.Equal(300, GetGold(world, "OrgWin", OwnerType.Org), precision: 6);
		}

		[Fact]
		void gold_with_no_controlling_orgs_goes_entirely_to_country() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddGold(world, "Loser", OwnerType.Country, 1000);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			Assert.Equal(700, GetGold(world, "Loser", OwnerType.Country), precision: 6);
			Assert.Equal(300, GetGold(world, "Winner", OwnerType.Country), precision: 6);
		}

		[Fact]
		void same_month_peace_transfers_no_gold() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddGold(world, "Loser", OwnerType.Country, 1000);
			AddGold(world, "Winner", OwnerType.Country, 0);

			Wars.ResolvePeace(world, warId, DeclareTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			Assert.Equal(1000, GetGold(world, "Loser", OwnerType.Country));
			Assert.Equal(0, GetGold(world, "Winner", OwnerType.Country));
		}

		[Fact]
		void control_shifts_top_first_with_fractions() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			AddControl(world, "OrgWinTop", "Winner", 40);
			AddControl(world, "OrgWinLow", "Winner", 20);
			AddControl(world, "OrgLoseTop", "Loser", 40);
			AddControl(world, "OrgLoseLow", "Loser", 20);

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			Assert.Equal(42, ControlQuery.GetOrgControlInCountry(world, "OrgWinTop", "Winner"));
			Assert.Equal(21, ControlQuery.GetOrgControlInCountry(world, "OrgWinLow", "Winner"));
			Assert.Equal(36, ControlQuery.GetOrgControlInCountry(world, "OrgLoseTop", "Loser"));
			Assert.Equal(18, ControlQuery.GetOrgControlInCountry(world, "OrgLoseLow", "Loser"));
		}

		[Fact]
		void winner_boost_with_base_effect_near_full_pool_stays_at_or_below_max() {
			var world = new World();
			Wars.DeclareWar(world, "Winner", "Loser", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			// base_* only: ApplyChangeControl alone would create permanent without counting base → overflow
			AddControl(world, "OrgWin", "Winner", 96, "base_OrgWin");
			AddControl(world, "OrgLose", "Loser", 10, "base_OrgLose");

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			Assert.True(ControlQuery.GetTotalControlInCountry(world, "Winner") <= 100);
			// desired = Round(96 * 0.05) = 5, room = 4 → +4 → total 100
			Assert.Equal(100, ControlQuery.GetTotalControlInCountry(world, "Winner"));
			Assert.Equal(100, ControlQuery.GetOrgControlInCountry(world, "OrgWin", "Winner"));
		}

		[Fact]
		void progress_zero_stop_war_skips_transfer_gold_and_control() {
			var world = new World();
			Wars.DeclareWar(world, "A", "B", DeclareTime);
			// progress stays 0

			AddOwnership(world, "p_b", "B");
			AddOccupation(world, "p_b", "A");
			AddOwnership(world, "p_a", "A");
			AddOccupation(world, "p_a", "");

			AddControl(world, "OrgA", "A", 40);
			AddControl(world, "OrgB", "B", 40);
			AddGold(world, "A", OwnerType.Country, 500);
			AddGold(world, "B", OwnerType.Country, 500);

			bool result = Wars.StopWar(world, "A", PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

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
			Wars.DeclareWar(world, "A", "B", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);
			CountryRelations.SetRelation(world, "A", "B", RelationKind.Rival);

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			Assert.Equal(RelationKind.Rival, CountryRelations.GetRelation(world, "A", "B"));
		}

		[Fact]
		void resolve_peace_creates_war_resolved_log_event_with_winner_and_loser() {
			var world = new World();
			Wars.DeclareWar(world, "Attacker", "Defender", DeclareTime);
			string warId = GetOnlyWarId(world);
			SetProgress(world, warId, 50);

			Wars.ResolvePeace(world, warId, PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

			WarResolvedApplied applied = Assert.Single(GetComponents<WarResolvedApplied>(world));
			Assert.Equal("Attacker", applied.WinnerCountryId);
			Assert.Equal("Defender", applied.LoserCountryId);
		}

		[Fact]
		void progress_zero_stop_war_creates_no_war_resolved_log_event() {
			var world = new World();
			Wars.DeclareWar(world, "A", "B", DeclareTime);
			// progress stays 0

			Wars.StopWar(world, "A", PeaceTime, new Random(1), DefaultSettings(), EmptyCenters(), 100);

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
			world.Add(eventEntity, new WarResolvedApplied { WinnerCountryId = "Attacker", LoserCountryId = "Defender" });
			var state = new VisualState();
			var converter = new VisualStateConverter(state);

			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			GameLogEntry entry = Assert.Single(state.GameLog.Entries);
			Assert.Equal(GameLogEntryKind.WarResolved, entry.Kind);
			Assert.Equal("Attacker", entry.CountryId);
			Assert.Equal("Defender", entry.TargetCountryId);

			CleanupEffectNotificationsSystem.UpdateWarResolved(world);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);
			Assert.Single(state.GameLog.Entries);
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
