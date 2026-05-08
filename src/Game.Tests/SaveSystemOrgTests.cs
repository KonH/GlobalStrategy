using System;
using ECS;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SaveSystemOrgTests {
		static World BuildWorld(string orgId = "Illuminati") {
			var world = new World();

			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = orgId, DisplayName = orgId });

			int timeEntity = world.Create();
			world.Add(timeEntity, new GameTime {
				CurrentTime = new DateTime(1882, 6, 15),
				IsPaused = false,
				MultiplierIndex = 0
			});

			return world;
		}

		[Fact]
		void snapshot_header_has_org_id() {
			var world = BuildWorld();
			var snapshot = SaveSystem.BuildSnapshot(world);
			Assert.Equal("Illuminati", snapshot.Header.OrganizationId);
		}

		[Fact]
		void save_name_uses_org_id_and_date() {
			var world = BuildWorld();
			var snapshot = SaveSystem.BuildSnapshot(world);
			Assert.StartsWith("Illuminati_1882-06-15", snapshot.Header.SaveName);
		}

		[Fact]
		void snapshot_header_game_date_is_correct() {
			var world = BuildWorld();
			var snapshot = SaveSystem.BuildSnapshot(world);
			Assert.Equal(new DateTime(1882, 6, 15), snapshot.Header.GameDate);
		}
	}
}
