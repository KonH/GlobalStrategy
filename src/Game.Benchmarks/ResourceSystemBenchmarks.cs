using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// The primary "resource update flow" target the resource-collector pipeline introduces:
	// the ordered resolve-then-apply pass over every configured resourceId, plus the
	// unordered fallback pass. No [IterationSetup] needed - previousTime/currentTime are
	// explicit parameters here, not read off GameTime, so a fixed month-boundary pair reused
	// every iteration reliably re-triggers the expensive path every single call.
	[MemoryDiagnoser]
	public class ResourceSystemBenchmarks {
		// Public so BenchmarkFixtureCorrectnessTests exercises the exact same date pairs this
		// benchmark uses, rather than a copy that could silently drift out of sync.
		public static readonly DateTime RegularPrevious = new DateTime(1880, 6, 15);
		public static readonly DateTime RegularCurrent = new DateTime(1880, 6, 16);
		public static readonly DateTime BoundaryPrevious = new DateTime(1880, 6, 30);
		public static readonly DateTime BoundaryCurrent = new DateTime(1880, 7, 1);

		World _world = null!;
		ResourceCollectorRegistry _registry = null!;
		IReadOnlyList<string> _resourceIdUpdateOrder = null!;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_registry = fixture.CollectorRegistry;
			_resourceIdUpdateOrder = fixture.ResourceIdUpdateOrder;
		}

		[Benchmark]
		public void Update_RegularDay() {
			ResourceSystem.Update(_world, RegularPrevious, RegularCurrent, _registry, _resourceIdUpdateOrder);
		}

		[Benchmark]
		public void Update_MonthBoundary() {
			ResourceSystem.Update(_world, BoundaryPrevious, BoundaryCurrent, _registry, _resourceIdUpdateOrder);
		}
	}
}
