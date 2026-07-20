using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// org_score is a collector-driven resource, same shape as country_score - there is no
	// standalone OrgScoreSystem class anymore (superseded by OrgScoreCollector). Its Compute
	// scans every ControlEffect and reads country_score per controlled country via
	// ResourceQuery.GetValue - a second O(countries x scan) shape worth watching alongside
	// CountryPopulationCollector's.
	[MemoryDiagnoser]
	public class OrgScoreCollectorBenchmarks {
		World _world = null!;
		IResourceCollector _collector = null!;
		string _orgId = null!;
		double _currentValue;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_collector = fixture.CollectorRegistry.Resolve(OrgScoreCollector.Id);
			_orgId = fixture.FirstOrgId;
			_currentValue = ResourceQuery.GetValue(_world, _orgId, ResourceDefinitions.OrgScore);
		}

		[Benchmark]
		public double Compute() {
			return _collector.Compute(_orgId, _currentValue, _world);
		}
	}
}
