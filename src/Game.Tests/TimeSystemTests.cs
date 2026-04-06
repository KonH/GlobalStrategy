using System;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class TimeSystemTests {
		static readonly int[] Multipliers = { 1, 24, 720 };
		static readonly DateTime Start = new DateTime(1880, 1, 1);

		static (World world, int entity) CreateWorld(bool paused = false, int multiplierIndex = 0) {
			var world = new World();
			int e = world.Create();
			world.Add(e, new GameTime {
				CurrentTime = Start,
				IsPaused = paused,
				MultiplierIndex = multiplierIndex,
				AccumulatedHours = 0f
			});
			return (world, e);
		}

		static void Run(World world, int entity, float deltaTime,
			ReadCommands<PauseCommand> pause = default,
			ReadCommands<UnpauseCommand> unpause = default,
			ReadCommands<ChangeTimeMultiplierCommand> speed = default) {
			TimeSystem.Update(world, entity, deltaTime, Multipliers, pause, unpause, speed);
		}

		[Fact]
		void time_does_not_advance_when_paused() {
			var (world, e) = CreateWorld(paused: true);
			Run(world, e, 10f);
			Assert.Equal(Start, world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void partial_deltatime_does_not_advance_time() {
			var (world, e) = CreateWorld();
			Run(world, e, 0.016f); // one frame at 60fps — less than 1 hour at x1
			Assert.Equal(Start, world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void time_advances_by_one_hour_after_one_second_at_x1() {
			var (world, e) = CreateWorld();
			Run(world, e, 1f);
			Assert.Equal(Start.AddHours(1), world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void accumulation_across_frames_triggers_advance() {
			var (world, e) = CreateWorld();
			// 63 frames × 0.016s ≈ 1.008s → should trigger 1 hour advance
			for (int i = 0; i < 63; i++)
				Run(world, e, 0.016f);
			Assert.Equal(Start.AddHours(1), world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void time_advances_by_24_hours_after_one_second_at_x2() {
			var (world, e) = CreateWorld(multiplierIndex: 1);
			Run(world, e, 1f);
			Assert.Equal(Start.AddHours(24), world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void time_advances_by_720_hours_after_one_second_at_x3() {
			var (world, e) = CreateWorld(multiplierIndex: 2);
			Run(world, e, 1f);
			Assert.Equal(Start.AddHours(720), world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void pause_command_stops_time() {
			var (world, e) = CreateWorld();
			var pause = new ReadCommands<PauseCommand>(new[] { new PauseCommand() });
			Run(world, e, 1f, pause: pause);
			Assert.True(world.Get<GameTime>(e).IsPaused);
			Assert.Equal(Start, world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void unpause_command_resumes_time() {
			var (world, e) = CreateWorld(paused: true);
			var unpause = new ReadCommands<UnpauseCommand>(new[] { new UnpauseCommand() });
			Run(world, e, 1f, unpause: unpause);
			Assert.False(world.Get<GameTime>(e).IsPaused);
			Assert.Equal(Start.AddHours(1), world.Get<GameTime>(e).CurrentTime);
		}

		[Fact]
		void speed_change_takes_effect_immediately() {
			var (world, e) = CreateWorld(multiplierIndex: 0);
			var speed = new ReadCommands<ChangeTimeMultiplierCommand>(
				new[] { new ChangeTimeMultiplierCommand(1) });
			Run(world, e, 1f, speed: speed);
			Assert.Equal(1, world.Get<GameTime>(e).MultiplierIndex);
			Assert.Equal(Start.AddHours(24), world.Get<GameTime>(e).CurrentTime);
		}
	}
}
