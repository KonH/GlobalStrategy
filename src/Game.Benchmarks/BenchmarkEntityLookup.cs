using System;
using ECS;

namespace GS.Game.Benchmarks {
	// Shared singleton-entity lookup for benchmark [GlobalSetup] methods - mirrors the
	// documented "Singleton entities" ECS pattern (.claude/rules/unity/ecs_patterns.md).
	static class BenchmarkEntityLookup {
		public static int FindEntityWith<T>(IReadOnlyWorld world) {
			int[] required = { TypeId<T>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(required, null)) {
				if (archetype.Count > 0) {
					return archetype.Entities[0];
				}
			}
			throw new InvalidOperationException($"No entity has component {typeof(T).Name}.");
		}
	}
}
