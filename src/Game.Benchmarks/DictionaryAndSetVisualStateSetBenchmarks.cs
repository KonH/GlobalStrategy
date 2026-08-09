using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using GS.Main;

using GS.Game.Systems;

namespace GS.Game.Benchmarks {
	[MemoryDiagnoser]
	public class DictionaryAndSetVisualStateSetBenchmarks {
		ProvinceOwnershipState _ownership = null!;
		ProvinceOccupationState _occupation = null!;
		CountryScoreState _score = null!;
		WorldCountriesState _worldCountries = null!;

		Dictionary<string, string> _ownershipBaseline = null!;
		Dictionary<string, string> _ownershipAlt = null!;
		Dictionary<string, string> _occupationBaseline = null!;
		Dictionary<string, string> _occupationAlt = null!;
		Dictionary<string, double> _scoreBaseline = null!;
		Dictionary<string, double> _scoreAlt = null!;
		HashSet<string> _worldCountriesBaseline = null!;
		HashSet<string> _worldCountriesAlt = null!;

		bool _ownershipToggle;
		bool _occupationToggle;
		bool _scoreToggle;
		bool _worldCountriesToggle;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			var visualState = fixture.Logic.VisualState;

			_ownership = visualState.ProvinceOwnership;
			_ownershipBaseline = new Dictionary<string, string>(_ownership.OwnerByProvinceId);
			_ownershipAlt = new Dictionary<string, string>(_ownershipBaseline) {
				["bench_province"] = "bench_country"
			};

			_occupation = visualState.ProvinceOccupation;
			_occupationBaseline = new Dictionary<string, string>(_occupation.OccupierByProvinceId);
			_occupationAlt = new Dictionary<string, string>(_occupationBaseline) {
				["bench_province"] = "bench_country"
			};

			_score = visualState.CountryScore;
			_scoreBaseline = new Dictionary<string, double>(_score.ScoreByCountryId);
			_scoreAlt = new Dictionary<string, double>(_scoreBaseline) {
				["bench_country"] = 999d
			};

			_worldCountries = visualState.WorldCountries;
			_worldCountriesBaseline = new HashSet<string>(_worldCountries.CountryIds);
			_worldCountriesAlt = new HashSet<string>(_worldCountriesBaseline) { "bench_country" };

			_ownership.Set(_ownershipBaseline);
			_occupation.Set(_occupationBaseline);
			_score.Set(_scoreBaseline);
			_worldCountries.Set(_worldCountriesBaseline);
		}

		[Benchmark]
		public void ProvinceOwnershipState_NoOp() =>
			_ownership.Set(new Dictionary<string, string>(_ownershipBaseline));

		[Benchmark]
		public void ProvinceOwnershipState_Update() {
			_ownershipToggle = !_ownershipToggle;
			_ownership.Set(_ownershipToggle ? _ownershipAlt : _ownershipBaseline);
		}

		[Benchmark]
		public void ProvinceOccupationState_NoOp() =>
			_occupation.Set(new Dictionary<string, string>(_occupationBaseline));

		[Benchmark]
		public void ProvinceOccupationState_Update() {
			_occupationToggle = !_occupationToggle;
			_occupation.Set(_occupationToggle ? _occupationAlt : _occupationBaseline);
		}

		[Benchmark]
		public void CountryScoreState_NoOp() =>
			_score.Set(new Dictionary<string, double>(_scoreBaseline));

		[Benchmark]
		public void CountryScoreState_Update() {
			_scoreToggle = !_scoreToggle;
			_score.Set(_scoreToggle ? _scoreAlt : _scoreBaseline);
		}

		[Benchmark]
		public void WorldCountriesState_NoOp() =>
			_worldCountries.Set(new HashSet<string>(_worldCountriesBaseline));

		[Benchmark]
		public void WorldCountriesState_Update() {
			_worldCountriesToggle = !_worldCountriesToggle;
			_worldCountries.Set(_worldCountriesToggle ? _worldCountriesAlt : _worldCountriesBaseline);
		}
	}
}
