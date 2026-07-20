using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// Cheap single-lookup collector (reads country_population via ResourceQuery.GetValue).
	[MemoryDiagnoser]
	public class CountryScoreCollectorBenchmarks {
		World _world = null!;
		IResourceCollector _collector = null!;
		string _countryId = null!;
		double _currentValue;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_collector = fixture.CollectorRegistry.Resolve(CountryScoreCollector.Id);
			_countryId = fixture.FirstCountryId;
			_currentValue = ResourceQuery.GetValue(_world, _countryId, ResourceDefinitions.CountryScore);
		}

		[Benchmark]
		public double Compute() {
			return _collector.Compute(_countryId, _currentValue, _world);
		}
	}
}
