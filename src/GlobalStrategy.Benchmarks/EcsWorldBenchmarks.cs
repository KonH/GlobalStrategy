using BenchmarkDotNet.Attributes;
using ECS;

namespace GlobalStrategy.Benchmarks;

[MemoryDiagnoser]
public class EcsWorldBenchmarks {
	[Params(1_000, 10_000)]
	public int EntityCount { get; set; }

	World _queryWorld = null!;
	int[] _entities = null!;
	int _sink;

	[GlobalSetup]
	public void Setup() {
		_queryWorld = new World();
		_entities = new int[EntityCount];

		for (int i = 0; i < EntityCount; i++) {
			int entity = _queryWorld.Create();
			_queryWorld.Add(entity, new BenchPosition(i, i));
			_queryWorld.Add(entity, new BenchVelocity(1, -1));

			if (i % 4 == 0) {
				_queryWorld.Add(entity, new BenchHealth(100));
			}

			_entities[i] = entity;
		}
	}

	[Benchmark]
	public int CreateEntities() {
		var world = new World();
		int lastEntity = 0;

		for (int i = 0; i < EntityCount; i++) {
			lastEntity = world.Create();
		}

		return lastEntity;
	}

	[Benchmark]
	public int AddTwoComponents() {
		var world = new World();
		int lastEntity = 0;

		for (int i = 0; i < EntityCount; i++) {
			lastEntity = world.Create();
			world.Add(lastEntity, new BenchPosition(i, i));
			world.Add(lastEntity, new BenchVelocity(1, -1));
		}

		return lastEntity;
	}

	[Benchmark]
	public int QueryTwoComponentsAndMutate() {
		int visited = 0;

		_queryWorld.Query<BenchPosition, BenchVelocity>(
			static (int entity, ref BenchPosition position, ref BenchVelocity velocity) => {
				position = new BenchPosition(position.X + velocity.X, position.Y + velocity.Y);
			});

		_queryWorld.Query<BenchPosition>(
			(int entity, ref BenchPosition position) => {
				visited++;
				_sink ^= entity ^ position.X;
			});

		return visited + _sink;
	}

	[Benchmark]
	public int GetComponentByEntity() {
		int total = 0;

		foreach (int entity in _entities) {
			ref BenchPosition position = ref _queryWorld.Get<BenchPosition>(entity);
			total += position.X;
		}

		return total;
	}

	readonly record struct BenchPosition(int X, int Y);
	readonly record struct BenchVelocity(int X, int Y);
	readonly record struct BenchHealth(int Value);
}
