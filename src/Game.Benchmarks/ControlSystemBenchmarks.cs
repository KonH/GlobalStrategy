using System;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// Unchanged system - not touched by the resource-collector pipeline. ControlSystem.Update
	// early-returns unless previousTime/currentTime cross a month boundary (it only pays
	// monthly control income), so the call shape must use a real month-boundary date pair -
	// a same-month day step would silently benchmark the cheap no-op early-return instead.
	[MemoryDiagnoser]
	public class ControlSystemBenchmarks {
		static readonly DateTime MonthBoundaryPrevious = new DateTime(1880, 6, 30);
		static readonly DateTime MonthBoundaryCurrent = new DateTime(1880, 7, 1);

		World _world = null!;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
		}

		[Benchmark]
		public void Update() {
			ControlSystem.Update(_world, MonthBoundaryPrevious, MonthBoundaryCurrent);
		}
	}
}
