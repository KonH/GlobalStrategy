using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class SelectPlayerCountrySystemTests {
		static World CreateWorld(out int russia, out int france) {
			var world = new World();
			russia = world.Create();
			world.Add(russia, new Country("Russian_Empire"));
			world.Add(russia, new Player());
			france = world.Create();
			world.Add(france, new Country("France"));
			return world;
		}

		static void Run(World world, string countryId) {
			var commands = new ReadCommands<SelectPlayerCountryCommand>(
				new[] { new SelectPlayerCountryCommand(countryId) });
			SelectPlayerCountrySystem.Update(world, commands);
		}

		[Fact]
		void player_moves_to_target_country() {
			var world = CreateWorld(out int russia, out int france);
			Run(world, "France");
			Assert.False(world.Has<Player>(russia));
			Assert.True(world.Has<Player>(france));
		}

		[Fact]
		void player_stays_when_already_on_target() {
			var world = CreateWorld(out int russia, out _);
			Run(world, "Russian_Empire");
			Assert.True(world.Has<Player>(russia));
		}

		[Fact]
		void no_commands_leaves_player_unchanged() {
			var world = CreateWorld(out int russia, out _);
			var empty = new ReadCommands<SelectPlayerCountryCommand>(System.Array.Empty<SelectPlayerCountryCommand>());
			SelectPlayerCountrySystem.Update(world, empty);
			Assert.True(world.Has<Player>(russia));
		}
	}
}
