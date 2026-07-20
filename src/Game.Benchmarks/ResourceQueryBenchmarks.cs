using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Configs;
using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	// Shared helper called once per collector resolve per country - a full archetype scan
	// per call, so its own cost multiplies across every caller - worth watching independently.
	[MemoryDiagnoser]
	public class ResourceQueryBenchmarks {
		World _world = null!;
		string _countryId = null!;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_world = fixture.Logic.World;
			_countryId = fixture.FirstCountryId;
		}

		[Benchmark]
		public double GetValue() {
			return ResourceQuery.GetValue(_world, _countryId, ResourceDefinitions.CountryPopulation);
		}
	}
}
