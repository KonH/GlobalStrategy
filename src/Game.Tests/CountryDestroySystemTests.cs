using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CountryDestroySystemTests {
		readonly CountryRelations _relations = new CountryRelations();
		readonly ResourceQuery _resources = new ResourceQuery();

		static int AddCountry(World world, string countryId, bool selected = false) {
			int entity = world.Create();
			world.Add(entity, new Country(countryId));
			if (selected) {
				world.Add(entity, new IsSelected());
			}
			return entity;
		}

		static void AddProvince(World world, string provinceId, string ownerId) {
			int entity = world.Create();
			world.Add(entity, new ProvinceOwnership { ProvinceId = provinceId, OwnerId = ownerId });
		}

		static void AddControl(World world, string orgId, string countryId, int value, string effectId) {
			int entity = world.Create();
			world.Add(entity, new ControlEffect {
				OrgId = orgId,
				CountryId = countryId,
				Value = value,
				EffectId = effectId
			});
		}

		static int CountDestroyedApplied(World world) {
			int count = 0;
			int[] required = { TypeId<CountryDestroyedApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				count += arch.Count;
			}
			return count;
		}

		static List<CountryDestroyedApplied> GetDestroyedApplied(World world) {
			var result = new List<CountryDestroyedApplied>();
			int[] required = { TypeId<CountryDestroyedApplied>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				CountryDestroyedApplied[] applied = arch.GetColumn<CountryDestroyedApplied>();
				for (int i = 0; i < arch.Count; i++) {
					result.Add(applied[i]);
				}
			}
			return result;
		}

		[Fact]
		void country_with_provinces_is_not_destroyed() {
			var world = new World();
			int country = AddCountry(world, "Prussia");
			AddProvince(world, "prov_1", "Prussia");

			Assert.False(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Prussia"));
			Assert.False(world.Has<IsDestroyed>(country));
			Assert.Empty(GetDestroyedApplied(world));
		}

		[Fact]
		void last_province_lost_destroys_country_emits_event_and_clears_selection_control_relations() {
			var world = new World();
			int destroyedCountry = AddCountry(world, "Prussia", selected: true);
			AddCountry(world, "Austria");
			AddCountry(world, "France");
			AddControl(world, "OrgA", "Prussia", 20, "a");
			AddControl(world, "OrgB", "Prussia", 5, "b");
			AddControl(world, "OrgA", "Austria", 10, "keep");
			_relations.SetRelation(world, "Prussia", "Austria", RelationKind.Friend);
			_relations.SetRelation(world, "Prussia", "France", RelationKind.Rival);
			_relations.SetRelation(world, "Austria", "France", RelationKind.Friend);

			Assert.True(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Prussia"));

			Assert.True(world.Has<IsDestroyed>(destroyedCountry));
			Assert.True(world.Has<Country>(destroyedCountry));
			Assert.False(world.Has<IsSelected>(destroyedCountry));
			Assert.Equal(0, ControlQuery.GetTotalControlInCountry(world, "Prussia"));
			Assert.Equal(10, ControlQuery.GetTotalControlInCountry(world, "Austria"));
			Assert.Null(_relations.GetRelation(world, "Prussia", "Austria"));
			Assert.Null(_relations.GetRelation(world, "Prussia", "France"));
			Assert.Equal(RelationKind.Friend, _relations.GetRelation(world, "Austria", "France"));
			CountryDestroyedApplied applied = Assert.Single(GetDestroyedApplied(world));
			Assert.Equal("Prussia", applied.CountryId);
		}

		[Fact]
		void second_destroy_call_is_idempotent() {
			var world = new World();
			AddCountry(world, "Prussia");

			Assert.True(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Prussia"));
			Assert.False(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Prussia"));
			Assert.Equal(1, CountDestroyedApplied(world));
		}

		[Fact]
		void destroy_all_zero_province_countries_destroys_only_empty_owners() {
			var world = new World();
			AddCountry(world, "Empty");
			AddCountry(world, "HasLand");
			AddProvince(world, "prov_1", "HasLand");

			int newlyDestroyed = CountryDestroySystem.DestroyAllZeroProvinceCountries(world, _relations);

			Assert.Equal(1, newlyDestroyed);
			Assert.True(CountryDestroySystem.IsCountryDestroyed(world, "Empty"));
			Assert.False(CountryDestroySystem.IsCountryDestroyed(world, "HasLand"));
		}

		[Fact]
		void select_destroyed_country_does_not_add_is_selected() {
			var world = new World();
			int alive = AddCountry(world, "Alive", selected: true);
			int dead = AddCountry(world, "Dead");
			world.Add(dead, new IsDestroyed());

			SelectCountrySystem.Update(world, new ReadCommands<SelectCountryCommand>(
				new[] { new SelectCountryCommand("Dead") }));

			Assert.True(world.Has<IsSelected>(alive));
			Assert.False(world.Has<IsSelected>(dead));
		}

		[Fact]
		void select_alive_country_still_works_and_empty_id_clears() {
			var world = new World();
			int first = AddCountry(world, "First", selected: true);
			int second = AddCountry(world, "Second");

			SelectCountrySystem.Update(world, new ReadCommands<SelectCountryCommand>(
				new[] { new SelectCountryCommand("Second") }));
			Assert.False(world.Has<IsSelected>(first));
			Assert.True(world.Has<IsSelected>(second));

			SelectCountrySystem.Update(world, new ReadCommands<SelectCountryCommand>(
				new[] { new SelectCountryCommand("") }));
			Assert.False(world.Has<IsSelected>(first));
			Assert.False(world.Has<IsSelected>(second));
		}

		[Fact]
		void country_destroyed_event_survives_until_next_tick_cleanup_and_fifo_projects() {
			var world = new World();
			int gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = new DateTime(1880, 1, 1) });
			int localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "Org", DisplayName = "Org" });
			AddCountry(world, "Prussia");
			AddCountry(world, "Austria");
			AddProvince(world, "prov_a", "Austria");

			Assert.True(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Prussia"));
			Assert.Single(GetDestroyedApplied(world));

			CleanupEffectNotificationsSystem.UpdateActionEffects(world);
			Assert.Single(GetDestroyedApplied(world));

			var state = new VisualState();
			var converter = new VisualStateConverter(state, _resources, _relations);
			converter.Update(0, world, gameTimeEntity, localeEntity, orgEntity);

			CountryDestroyedSnapshotState snapshot = Assert.Single(state.CountryDestroyedResults.Entries);
			Assert.Equal("Prussia", snapshot.CountryId);
			Assert.Contains("Prussia", state.WorldCountries.CountryIds);
			Assert.Contains("Prussia", state.WorldCountries.DestroyedCountryIds);
			Assert.DoesNotContain("Prussia", GameCompletionSystem.GetAvailableCountryIds(world));

			Assert.True(state.CountryDestroyedResults.TryPeek(out CountryDestroyedSnapshotState? peeked));
			Assert.Equal("Prussia", peeked!.CountryId);
			state.CountryDestroyedResults.AcknowledgeCurrent();
			Assert.Empty(state.CountryDestroyedResults.Entries);

			CleanupEffectNotificationsSystem.UpdateCountryDestroyed(world);
			Assert.Empty(GetDestroyedApplied(world));
		}

		[Fact]
		void peace_transfer_emptying_loser_destroys_loser_once() {
			var world = new World();
			AddCountry(world, "Winner");
			AddCountry(world, "Loser");
			AddProvince(world, "prov_1", "Loser");
			Wars.DeclareWar(world, _resources, "Winner", "Loser", new DateTime(1880, 1, 1));
			string warId = GetOnlyWarId(world);
			ResourceMutations.TrySetValue(_resources, world, warId, ResourceDefinitions.WarProgress, 50, out _);
			int occupation = world.Create();
			world.Add(occupation, new ProvinceOccupation { ProvinceId = "prov_1", OccupierId = "Winner" });

			var settings = new GameSettings {
				PeaceProvinceTransferMinPercent = 100,
				PeaceProvinceTransferMaxPercent = 100,
				PeaceGoldPerMonth = 0,
				PeaceWinnerControlIncreaseFraction = 0,
				PeaceLoserControlDecreaseFraction = 0
			};
			var losers = new List<string>();
			Wars.ResolvePeace(
				world, _resources, warId, new DateTime(1880, 4, 1), new Random(1), settings,
				new ProvinceTopology(new ProvinceConfig()), new Dictionary<string, (double Lon, double Lat)>(), 100,
				territoryLosers: losers);

			Assert.Equal(new[] { "Loser" }, losers);
			Assert.True(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Loser"));
			Assert.False(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, "Loser"));
			Assert.True(CountryDestroySystem.IsCountryDestroyed(world, "Loser"));
			Assert.Equal(1, CountDestroyedApplied(world));
			Assert.False(CountryDestroySystem.IsCountryDestroyed(world, "Winner"));
		}

		[Fact]
		void change_owner_stripping_last_province_destroys_old_owner_only() {
			var world = new World();
			AddCountry(world, "Old");
			AddCountry(world, "New");
			AddProvince(world, "prov_1", "Old");
			AddProvince(world, "prov_2", "New");

			var (changed, oldOwnerId) = ProvinceOwnershipSystem.ChangeOwner(world, "prov_1", "New");
			Assert.True(changed);
			Assert.Equal("Old", oldOwnerId);

			Assert.True(CountryDestroySystem.TryDestroyIfNoProvinces(world, _relations, oldOwnerId));
			Assert.True(CountryDestroySystem.IsCountryDestroyed(world, "Old"));
			Assert.False(CountryDestroySystem.IsCountryDestroyed(world, "New"));
		}

		static string GetOnlyWarId(World world) {
			int[] required = { TypeId<War>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<War>()[0].WarId;
				}
			}
			throw new InvalidOperationException("no war");
		}
	}
}
