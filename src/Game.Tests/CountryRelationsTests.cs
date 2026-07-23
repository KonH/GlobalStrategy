using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class CountryRelationsTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static GameLogic BuildLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "Great_Britain", DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = "France", DisplayName = "France", IsAvailable = true },
					new CountryEntry { CountryId = "Germany", DisplayName = "Germany", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = "Illuminati",
						DisplayName = "Illuminati",
						HqCountryId = "Great_Britain",
						InitialGold = 1000.0
					}
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 2, 4 },
				AutoSaveInterval = "monthly"
			};
			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(new ResourceConfig { Resources = new List<ResourceDefinition>() }),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: "Illuminati",
				province: new StaticConfig<ProvinceConfig>(new ProvinceConfig()));
			return new GameLogic(ctx);
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
		void set_relation_creates_bidirectionally_queryable_friend_relation() {
			var world = new World();

			bool result = CountryRelations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			Assert.True(result);
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(world, "France", "Great_Britain"));
		}

		[Fact]
		void set_relation_with_opposite_kind_replaces_existing_pair() {
			var world = new World();
			CountryRelations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			bool result = CountryRelations.SetRelation(world, "Great_Britain", "France", RelationKind.Rival);

			Assert.True(result);
			Assert.Equal(RelationKind.Rival, CountryRelations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(1, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void remove_relation_clears_pair_from_both_directions() {
			var world = new World();
			CountryRelations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			bool removed = CountryRelations.RemoveRelation(world, "France", "Great_Britain");

			Assert.True(removed);
			Assert.Null(CountryRelations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(0, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void remove_relation_returns_false_when_no_pair_exists() {
			var world = new World();

			bool removed = CountryRelations.RemoveRelation(world, "Great_Britain", "France");

			Assert.False(removed);
		}

		[Fact]
		void set_relation_rejects_self_relation() {
			var world = new World();

			bool result = CountryRelations.SetRelation(world, "Great_Britain", "Great_Britain", RelationKind.Friend);

			Assert.False(result);
			Assert.Equal(0, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void get_relation_returns_null_for_self_relation() {
			var world = new World();

			Assert.Null(CountryRelations.GetRelation(world, "Great_Britain", "Great_Britain"));
		}

		[Fact]
		void get_relations_by_country_id_returns_independent_friend_and_rival_lists() {
			var world = new World();
			CountryRelations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);
			CountryRelations.SetRelation(world, "Germany", "Great_Britain", RelationKind.Rival);
			CountryRelations.SetRelation(world, "France", "Germany", RelationKind.Friend);

			var (friends, rivals) = CountryRelations.GetRelationsByCountryId(world, "Great_Britain");

			Assert.Single(friends);
			Assert.Contains("France", friends);
			Assert.Single(rivals);
			Assert.Contains("Germany", rivals);
		}

		[Fact]
		void debug_commands_set_and_clear_relation_through_game_logic() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugSetCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France", Kind = RelationKind.Friend });
			logic.Update(0f);

			Assert.Equal(RelationKind.Friend, CountryRelations.GetRelation(logic.World, "Great_Britain", "France"));

			logic.Commands.Push(new DebugSetCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France", Kind = RelationKind.Rival });
			logic.Update(0f);

			Assert.Equal(RelationKind.Rival, CountryRelations.GetRelation(logic.World, "Great_Britain", "France"));

			logic.Commands.Push(new DebugClearCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France" });
			logic.Update(0f);

			Assert.Null(CountryRelations.GetRelation(logic.World, "Great_Britain", "France"));
		}

		[Fact]
		void visual_state_reflects_relation_changes_for_selected_country() {
			var logic = BuildLogic();
			logic.Update(0f);
			logic.Commands.Push(new SelectCountryCommand { CountryId = "Great_Britain" });
			logic.Update(0f);

			logic.Commands.Push(new DebugSetCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France", Kind = RelationKind.Friend });
			logic.Update(0f);

			Assert.Contains("France", logic.VisualState.SelectedCountry.Relations.Friends);
			Assert.Empty(logic.VisualState.SelectedCountry.Relations.Rivals);
		}
	}
}
