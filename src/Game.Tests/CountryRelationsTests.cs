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
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
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
			var relations = new CountryRelations();
			var world = new World();

			bool result = relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			Assert.True(result);
			Assert.Equal(RelationKind.Friend, relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(RelationKind.Friend, relations.GetRelation(world, "France", "Great_Britain"));
		}

		[Fact]
		void set_relation_with_opposite_kind_replaces_existing_pair() {
			var relations = new CountryRelations();
			var world = new World();
			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			bool result = relations.SetRelation(world, "Great_Britain", "France", RelationKind.Rival);

			Assert.True(result);
			Assert.Equal(RelationKind.Rival, relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(1, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void remove_relation_clears_pair_from_both_directions() {
			var relations = new CountryRelations();
			var world = new World();
			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			bool removed = relations.RemoveRelation(world, "France", "Great_Britain");

			Assert.True(removed);
			Assert.Null(relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(0, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void remove_relation_returns_false_when_no_pair_exists() {
			var relations = new CountryRelations();
			var world = new World();

			bool removed = relations.RemoveRelation(world, "Great_Britain", "France");

			Assert.False(removed);
		}

		[Fact]
		void set_relation_rejects_self_relation() {
			var relations = new CountryRelations();
			var world = new World();

			bool result = relations.SetRelation(world, "Great_Britain", "Great_Britain", RelationKind.Friend);

			Assert.False(result);
			Assert.Equal(0, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void get_relation_returns_null_for_self_relation() {
			var relations = new CountryRelations();
			var world = new World();

			Assert.Null(relations.GetRelation(world, "Great_Britain", "Great_Britain"));
		}

		[Fact]
		void get_relations_by_country_id_returns_independent_friend_and_rival_lists() {
			var relations = new CountryRelations();
			var world = new World();
			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);
			relations.SetRelation(world, "Germany", "Great_Britain", RelationKind.Rival);
			relations.SetRelation(world, "France", "Germany", RelationKind.Friend);

			var (friends, rivals) = relations.GetRelationsByCountryId(world, "Great_Britain");

			Assert.Single(friends);
			Assert.Contains("France", friends);
			Assert.Single(rivals);
			Assert.Contains("Germany", rivals);
		}

		static int SeedVersionEntity(World world, int initialValue) {
			int e = world.Create();
			world.Add(e, new CountryRelationsVersion { Value = initialValue });
			return e;
		}

		[Fact]
		void set_relation_bumps_country_relations_version() {
			var relations = new CountryRelations();
			var world = new World();
			int versionEntity = SeedVersionEntity(world, 0);

			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			Assert.Equal(1, world.Get<CountryRelationsVersion>(versionEntity).Value);
		}

		[Fact]
		void remove_relation_bumps_country_relations_version() {
			var relations = new CountryRelations();
			var world = new World();
			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);
			int versionEntity = SeedVersionEntity(world, 5);

			bool removed = relations.RemoveRelation(world, "Great_Britain", "France");

			Assert.True(removed);
			Assert.Equal(6, world.Get<CountryRelationsVersion>(versionEntity).Value);
		}

		[Fact]
		void remove_relation_without_match_does_not_bump_version() {
			var relations = new CountryRelations();
			var world = new World();
			int versionEntity = SeedVersionEntity(world, 3);

			bool removed = relations.RemoveRelation(world, "Great_Britain", "France");

			Assert.False(removed);
			Assert.Equal(3, world.Get<CountryRelationsVersion>(versionEntity).Value);
		}

		[Fact]
		void set_relation_without_seeded_version_singleton_does_not_throw() {
			var relations = new CountryRelations();
			var world = new World();

			bool result = relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			Assert.True(result);
		}

		[Fact]
		void get_relation_caches_known_absence_without_warning() {
			var relations = new CountryRelations();
			var world = new World();
			int warnings = 0;
			relations.OnCacheMissWarning = _ => warnings++;

			Assert.Null(relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(0, warnings);

			Assert.Null(relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(0, warnings);
		}

		[Fact]
		void get_relation_warns_only_when_existing_relation_was_missing_from_cache() {
			var relations = new CountryRelations();
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new CountryRelation {
				Kind = RelationKind.Friend,
				LeftCountryId = "Great_Britain",
				RightCountryId = "France"
			});
			int warnings = 0;
			relations.OnCacheMissWarning = _ => warnings++;

			Assert.Equal(RelationKind.Friend, relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(1, warnings);

			Assert.Equal(RelationKind.Friend, relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Equal(1, warnings);
		}

		[Fact]
		void has_suitable_relation_target_uses_cache_and_invalidates_on_mutation() {
			var relations = new CountryRelations();
			var world = new World();
			world.Add(world.Create(), new Country("Great_Britain"));
			world.Add(world.Create(), new Country("France"));
			int warnings = 0;
			relations.OnCacheMissWarning = message => {
				if (message.Contains("HasSuitableRelationTarget")) {
					warnings++;
				}
			};

			Assert.True(relations.HasSuitableRelationTarget(world, "Great_Britain"));
			Assert.Equal(1, warnings);
			Assert.True(relations.HasSuitableRelationTarget(world, "Great_Britain"));
			Assert.Equal(1, warnings);

			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);

			Assert.False(relations.HasSuitableRelationTarget(world, "Great_Britain"));
			Assert.Equal(2, warnings);
			Assert.False(relations.HasSuitableRelationTarget(world, "Great_Britain"));
			Assert.Equal(2, warnings);

			relations.RemoveRelation(world, "Great_Britain", "France");

			Assert.True(relations.HasSuitableRelationTarget(world, "Great_Britain"));
			Assert.Equal(3, warnings);
		}

		[Fact]
		void debug_commands_set_and_clear_relation_through_game_logic() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugSetCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France", Kind = RelationKind.Friend });
			logic.Update(0f);

			Assert.Equal(RelationKind.Friend, logic.Relations.GetRelation(logic.World, "Great_Britain", "France"));

			logic.Commands.Push(new DebugSetCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France", Kind = RelationKind.Rival });
			logic.Update(0f);

			Assert.Equal(RelationKind.Rival, logic.Relations.GetRelation(logic.World, "Great_Britain", "France"));

			logic.Commands.Push(new DebugClearCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France" });
			logic.Update(0f);

			Assert.Null(logic.Relations.GetRelation(logic.World, "Great_Britain", "France"));
		}

		[Fact]
		void visual_state_reflects_relation_changes_for_selected_country() {
			var relations = new CountryRelations();
			var logic = BuildLogic();
			logic.Update(0f);
			logic.Commands.Push(new SelectCountryCommand { CountryId = "Great_Britain" });
			logic.Update(0f);

			logic.Commands.Push(new DebugSetCountryRelationCommand { CountryIdA = "Great_Britain", CountryIdB = "France", Kind = RelationKind.Friend });
			logic.Update(0f);

			Assert.Contains("France", logic.VisualState.SelectedCountry.Relations.Friends);
			Assert.Empty(logic.VisualState.SelectedCountry.Relations.Rivals);
		}

		[Fact]
		void remove_all_referencing_clears_both_sides_and_leaves_unrelated_pairs() {
			var relations = new CountryRelations();
			var world = new World();
			relations.SetRelation(world, "Great_Britain", "France", RelationKind.Friend);
			relations.SetRelation(world, "Great_Britain", "Germany", RelationKind.Rival);
			relations.SetRelation(world, "France", "Germany", RelationKind.Friend);

			int removed = relations.RemoveAllReferencing(world, "Great_Britain");

			Assert.Equal(2, removed);
			Assert.Null(relations.GetRelation(world, "Great_Britain", "France"));
			Assert.Null(relations.GetRelation(world, "Great_Britain", "Germany"));
			Assert.Equal(RelationKind.Friend, relations.GetRelation(world, "France", "Germany"));
			Assert.Equal(1, CountEntities<CountryRelation>(world));
		}

		[Fact]
		void suitable_relation_candidates_exclude_destroyed_countries() {
			var relations = new CountryRelations();
			var world = new World();
			world.Add(world.Create(), new Country("Great_Britain"));
			world.Add(world.Create(), new Country("France"));
			int dead = world.Create();
			world.Add(dead, new Country("Germany"));
			world.Add(dead, new IsDestroyed());

			List<string> candidates = relations.GetSuitableRelationCandidates(world, "Great_Britain");

			Assert.Contains("France", candidates);
			Assert.DoesNotContain("Germany", candidates);
			Assert.Empty(relations.GetSuitableRelationCandidates(world, "Germany"));
		}
	}
}
