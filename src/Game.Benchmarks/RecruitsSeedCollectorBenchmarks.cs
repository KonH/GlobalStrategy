using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	[MemoryDiagnoser]
	public class RecruitsSeedCollectorBenchmarks {
		World _world = null!;
		IResourceCollector _collector = null!;
		string _countryId = null!;
		double _currentValue;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_collector = fixture.CollectorRegistry.Resolve(RecruitsSeedCollector.Id);
			_countryId = fixture.FirstCountryId;
			_currentValue = ResourceQuery.GetValue(_world, _countryId, ResourceDefinitions.Recruits);
		}

		[Benchmark]
		public double Compute() {
			return _collector.Compute(_countryId, _currentValue, _world);
		}
	}
}
