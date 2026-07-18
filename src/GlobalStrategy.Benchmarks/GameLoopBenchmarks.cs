using BenchmarkDotNet.Attributes;
using GS.Main;

namespace GlobalStrategy.Benchmarks;

[MemoryDiagnoser]
public class GameLoopBenchmarks {
	[Params(30, 365)]
	public int TickCount { get; set; }

	GameLogic _logic = null!;

	[IterationSetup]
	public void Setup() {
		_logic = BenchmarkGameFactory.CreateGameLogic();
		_logic.Update(0f);
	}

	[Benchmark]
	public void UpdateFullGameLoop() {
		for (int i = 0; i < TickCount; i++) {
			_logic.Update(24f);
		}
	}
}
