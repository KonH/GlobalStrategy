using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class WarSystemTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static readonly DateTime Jan31 = new DateTime(1880, 1, 31, 23, 0, 0);
		static readonly DateTime Feb1  = new DateTime(1880, 2,  1,  0, 0, 0);
		static readonly DateTime Jan1  = new DateTime(1880, 1,  1,  0, 0, 0);
		static readonly DateTime Jan2  = new DateTime(1880, 1,  2,  0, 0, 0);

		static ResourceConfig WarProgressConfig() => new ResourceConfig {
			Resources = new List<ResourceDefinition> {
				new ResourceDefinition {
					ResourceId = ResourceDefinitions.WarProgress,
					RecordHistory = true,
					MinValue = -100,
					MaxValue = 100
				}
			}
		};

		static string AddWarWithProgress(World world, double value) {
			const string warId = "war_A_B_1";
			int warEntity = world.Create();
			world.Add(warEntity, new War { WarId = warId });
			int progressEntity = world.Create();
			world.Add(progressEntity, new ResourceOwner(warId, OwnerType.War));
			world.Add(progressEntity, new Resource {
				ResourceId = ResourceDefinitions.WarProgress,
				Value = value
			});
			world.Add(progressEntity, new ResourceHistory {
				History = new List<ResourceChangeEntry>()
			});
			return warId;
		}

		static ResourceHistory GetHistory(World world, string warId) {
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

		[Fact]
		void month_boundary_decays_progress_by_configured_amount() {
			var world = new World();
			string warId = AddWarWithProgress(world, 0);
			var config = WarProgressConfig();

			WarSystem.Update(world, Jan31, Feb1, 2.5, _resources, config);

			Assert.Equal(-2.5, _resources.GetValue(world, warId, ResourceDefinitions.WarProgress));
			ResourceHistory history = GetHistory(world, warId);
			Assert.Single(history.History);
			Assert.Equal("war_progress_decay", history.History[0].EffectId);
			Assert.Equal(-2.5, history.History[0].AppliedDelta);
			Assert.Equal(Feb1, history.History[0].Timestamp);
		}

		[Fact]
		void decay_floor_clamps_at_minus_100_and_does_not_go_lower() {
			var world = new World();
			string warId = AddWarWithProgress(world, -99);
			var config = WarProgressConfig();

			WarSystem.Update(world, Jan31, Feb1, 2.5, _resources, config);
			Assert.Equal(-100, _resources.GetValue(world, warId, ResourceDefinitions.WarProgress));

			WarSystem.Update(world, new DateTime(1880, 2, 28), new DateTime(1880, 3, 1), 2.5, _resources, config);
			Assert.Equal(-100, _resources.GetValue(world, warId, ResourceDefinitions.WarProgress));
		}

		[Fact]
		void no_month_boundary_means_no_change() {
			var world = new World();
			string warId = AddWarWithProgress(world, 10);
			var config = WarProgressConfig();

			WarSystem.Update(world, Jan1, Jan2, 2.5, _resources, config);

			Assert.Equal(10, _resources.GetValue(world, warId, ResourceDefinitions.WarProgress));
			Assert.Empty(GetHistory(world, warId).History);
		}
	}
}
