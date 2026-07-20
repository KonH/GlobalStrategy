using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// Pure O(1) function - a low/flat baseline for comparison against the other collectors.
	[MemoryDiagnoser]
	public class PopulationGrowthCollectorBenchmarks {
		World _world = null!;
		IResourceCollector _collector = null!;
		string _provinceId = null!;
		double _currentValue;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_collector = fixture.CollectorRegistry.Resolve(PopulationGrowthCollector.Id);
			_provinceId = fixture.FirstProvinceId;
			_currentValue = ResourceQuery.GetValue(_world, _provinceId, ResourceDefinitions.Population);
		}

		[Benchmark]
		public double Compute() {
			return _collector.Compute(_provinceId, _currentValue, _world);
		}
	}
}
