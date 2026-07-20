using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// The pipeline's own text flags this collector as a deliberately-accepted O(countries x
	// provinces) tradeoff - the most likely first target for /optimize-performance.
	[MemoryDiagnoser]
	public class CountryPopulationCollectorBenchmarks {
		World _world = null!;
		IResourceCollector _collector = null!;
		string _countryId = null!;
		double _currentValue;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_collector = fixture.CollectorRegistry.Resolve(CountryPopulationCollector.Id);
			_countryId = fixture.FirstCountryId;
			_currentValue = ResourceQuery.GetValue(_world, _countryId, ResourceDefinitions.CountryPopulation);
		}

		[Benchmark]
		public double Compute() {
			return _collector.Compute(_countryId, _currentValue, _world);
		}
	}
}
