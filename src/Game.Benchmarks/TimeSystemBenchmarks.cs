using System;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// Unchanged system - not touched by the resource-collector pipeline.
	[MemoryDiagnoser]
	public class TimeSystemBenchmarks {
		static readonly DateTime FixedDate = new DateTime(1880, 6, 15);

		World _world = null!;
		int _gameTimeEntity;
		int[] _speedMultipliers = null!;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_gameTimeEntity = fixture.GameTimeEntity;
			_speedMultipliers = fixture.SpeedMultipliers;
		}

		[Benchmark]
		public void Update() {
			// TimeSystem.Update advances GameTime.CurrentTime every call - BenchmarkDotNet
			// invokes this method millions of times per iteration batch (unrolled), so without
			// resetting the date each call, CurrentTime overflows past DateTime.MaxValue partway
			// through the pilot stage. Reset is a couple of struct field writes, negligible next
			// to TimeSystem.Update's own cost, and keeps every call measuring the same shape of
			// work regardless of invocation count.
			ref GameTime time = ref _world.Get<GameTime>(_gameTimeEntity);
			time.CurrentTime = FixedDate;
			time.AccumulatedHours = 0f;
			time.IsPaused = false;
			time.MultiplierIndex = 0;

			TimeSystem.Update(
				_world, _gameTimeEntity, 24f, _speedMultipliers,
				default(ReadCommands<PauseCommand>), default(ReadCommands<UnpauseCommand>),
				default(ReadCommands<ChangeTimeMultiplierCommand>));
		}
	}
}
