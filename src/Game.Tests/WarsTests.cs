using System;
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
	public class WarsTests {
		sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		static readonly DateTime DeclareTime = new DateTime(1880, 1, 1);

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
				new StaticConfig<ResourceConfig>(new ResourceConfig {
					Resources = new List<ResourceDefinition> {
						new ResourceDefinition {
							ResourceId = ResourceDefinitions.WarInitiative,
							SeedTarget = ResourceSeedTarget.Country
						}
					}
				}),
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
		void declare_war_creates_war_with_zero_progress_and_both_participants() {
			var world = new World();

			bool result = Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);

			Assert.True(result);
			Assert.True(Wars.IsInWar(world, "Great_Britain"));
			Assert.True(Wars.IsInWar(world, "France"));
			Assert.Equal(1, CountEntities<War>(world));
			Assert.Equal(2, CountEntities<WarParticipant>(world));

			string warId = GetSingle<War>(world).WarId;
			Assert.Equal(0, ResourceQuery.GetValue(world, warId, ResourceDefinitions.WarProgress));
			Assert.Equal(1, CountWarProgressResources(world));
			ResourceHistory history = GetWarProgressHistory(world, warId);
			Assert.NotNull(history.History);
			Assert.Empty(history.History);
		}

		[Fact]
		void declare_war_again_for_same_pair_is_a_no_op() {
			var world = new World();
			Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);

			bool result = Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime.AddDays(1));

			Assert.False(result);
			Assert.Equal(1, CountEntities<War>(world));
			Assert.Equal(2, CountEntities<WarParticipant>(world));
		}

		[Fact]
		void declare_war_rejects_self_war() {
			var world = new World();

			bool result = Wars.DeclareWar(world, "Great_Britain", "Great_Britain", DeclareTime);

			Assert.False(result);
			Assert.Equal(0, CountEntities<War>(world));
		}

		[Fact]
		void declare_war_blocks_country_already_in_any_war_regardless_of_side() {
			var world = new World();
			Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);

			bool result = Wars.DeclareWar(world, "Germany", "Great_Britain", DeclareTime.AddDays(1));

			Assert.False(result);
			Assert.Equal(1, CountEntities<War>(world));
			Assert.False(Wars.IsInWar(world, "Germany"));
		}

		[Fact]
		void stop_war_hard_deletes_war_and_both_participants() {
			var world = new World();
			Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);

			bool result = Wars.StopWar(
				world,
				"Great_Britain",
				DeclareTime,
				new Random(1),
				new GameSettings(),
				new Dictionary<string, (double Lon, double Lat)>(),
				100);

			Assert.True(result);
			Assert.Equal(0, CountEntities<War>(world));
			Assert.Equal(0, CountWarProgressResources(world));
			Assert.Equal(0, CountEntities<WarParticipant>(world));
			Assert.False(Wars.IsInWar(world, "Great_Britain"));
			Assert.False(Wars.IsInWar(world, "France"));
		}

		[Fact]
		void stop_war_on_country_not_in_any_war_is_a_no_op() {
			var world = new World();

			bool result = Wars.StopWar(
				world,
				"Great_Britain",
				DeclareTime,
				new Random(1),
				new GameSettings(),
				new Dictionary<string, (double Lon, double Lat)>(),
				100);

			Assert.False(result);
		}

		[Fact]
		void stop_war_releases_active_survivors_and_removes_battle_history() {
			var world = new World();
			int recruitsEntity = world.Create();
			world.Add(recruitsEntity, new ResourceOwner("Great_Britain", OwnerType.Country));
			world.Add(recruitsEntity, new Resource {
				ResourceId = ResourceDefinitions.Recruits,
				Value = 5
			});
			Wars.DeclareWar(world, "Great_Britain", "France", DeclareTime);
			string warId = GetSingle<War>(world).WarId;
			int activeBattle = world.Create();
			world.Add(activeBattle, new Battle {
				BattleId = "active",
				WarId = warId,
				State = BattleState.Active
			});
			int activeForce = world.Create();
			world.Add(activeForce, new BattleForce {
				BattleId = "active",
				CountryId = "Great_Britain",
				Side = WarParticipantKind.Attacker,
				Troops = 7,
				Casualties = 3
			});
			int finishedBattle = world.Create();
			world.Add(finishedBattle, new Battle {
				BattleId = "finished",
				WarId = warId,
				State = BattleState.Finished,
				Winner = WarParticipantKind.Attacker
			});
			int finishedForce = world.Create();
			world.Add(finishedForce, new BattleForce {
				BattleId = "finished",
				CountryId = "Great_Britain",
				Side = WarParticipantKind.Attacker,
				Troops = 9
			});

			Assert.True(Wars.StopWar(
				world,
				"Great_Britain",
				DeclareTime,
				new Random(1),
				new GameSettings(),
				new Dictionary<string, (double Lon, double Lat)>(),
				100));

			Assert.Equal(12, ResourceQuery.GetValue(world, "Great_Britain", ResourceDefinitions.Recruits));
			Assert.Equal(0, CountEntities<Battle>(world));
			Assert.Equal(0, CountEntities<BattleForce>(world));
			Assert.False(Wars.StopWar(
				world,
				"Great_Britain",
				DeclareTime,
				new Random(1),
				new GameSettings(),
				new Dictionary<string, (double Lon, double Lat)>(),
				100));
		}

		[Fact]
		void debug_commands_declare_and_stop_war_through_game_logic() {
			var logic = BuildLogic();
			logic.Update(0f);

			logic.Commands.Push(new DebugDeclareWarCommand { AttackerCountryId = "Great_Britain", DefenderCountryId = "France" });
			logic.Update(0f);

			Assert.True(Wars.IsInWar(logic.World, "Great_Britain"));
			Assert.True(Wars.IsInWar(logic.World, "France"));

			logic.Commands.Push(new DebugStopWarCommand { CountryId = "Great_Britain" });
			logic.Update(0f);

			Assert.False(Wars.IsInWar(logic.World, "Great_Britain"));
			Assert.False(Wars.IsInWar(logic.World, "France"));
		}

		static int CountWarProgressResources(World world) {
			int count = 0;
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						count++;
					}
				}
			}
			return count;
		}

		static ResourceHistory GetWarProgressHistory(World world, string warId) {
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value,
				TypeId<ResourceHistory>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == warId
						&& resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						return arch.GetColumn<ResourceHistory>()[i];
					}
				}
			}
			throw new InvalidOperationException("War progress resource not found.");
		}

		static T GetSingle<T>(World world) {
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<T>()[0];
				}
			}
			throw new InvalidOperationException($"No {typeof(T).Name} entity found.");
		}
	}
}
