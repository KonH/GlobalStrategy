using System;
using BenchmarkDotNet.Attributes;
using ECS;
using GS.Game.Components;
using GS.Main;

namespace GS.Game.Benchmarks {
	// The harness's headline number. Tick_MonthBoundary is the expensive tick that runs the
	// full ordered collector pipeline for every country/province; Tick_RegularDay is every
	// other day. [IterationSetup] forces a fixed pre-iteration date so the month-boundary
	// variant reliably exercises the expensive path every call (excluded from measurement -
	// only which branch each iteration takes is affected, not timing).
	[MemoryDiagnoser]
	public class FullTickBenchmarks {
		// Public so BenchmarkFixtureCorrectnessTests exercises the exact same dates this
		// benchmark uses, rather than a copy that could silently drift out of sync.
		public static readonly DateTime RegularDayDate = new DateTime(1880, 6, 15);
		public static readonly DateTime MonthBoundaryDate = new DateTime(1880, 6, 30);

		GameLogic _logic = null!;
		int _gameTimeEntity;

		[GlobalSetup]
		public void Setup() {
			var fixture = GameWorldFixture.Build();
			_logic = fixture.Logic;
			_gameTimeEntity = fixture.GameTimeEntity;
		}

		[IterationSetup(Target = nameof(Tick_RegularDay))]
		public void SetupRegularDay() {
			ForceDate(RegularDayDate);
		}

		[IterationSetup(Target = nameof(Tick_MonthBoundary))]
		public void SetupMonthBoundary() {
			ForceDate(MonthBoundaryDate);
		}

		[Benchmark]
		public void Tick_RegularDay() {
			_logic.Update(24f);
		}

		[Benchmark]
		public void Tick_MonthBoundary() {
			_logic.Update(24f);
		}

		void ForceDate(DateTime date) {
			ref GameTime time = ref _logic.World.Get<GameTime>(_gameTimeEntity);
			time.CurrentTime = date;
			time.AccumulatedHours = 0f;
			time.IsPaused = false;
			time.MultiplierIndex = 0;
		}
	}
}
