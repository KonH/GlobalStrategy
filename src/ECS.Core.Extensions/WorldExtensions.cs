using System;
using System.Collections.Generic;

namespace ECS.Extensions {
	public static class WorldExtensions {
		/// <summary>Returns the existing component if present; otherwise adds defaultValue and returns it.</summary>
		public static ref T GetOrAdd<T>(this World world, int entity, T defaultValue = default!)
			where T : struct {
			if (!world.Has<T>(entity))
				world.Add(entity, defaultValue);
			return ref world.Get<T>(entity);
		}

		/// <summary>Adds the same component value to every entity in the span.</summary>
		public static void AddRange<T>(this World world, ReadOnlySpan<int> entities, T comp) {
			foreach (int entity in entities)
				world.Add(entity, comp);
		}

		/// <summary>Destroys every currently alive entity in the world.</summary>
		public static void DestroyAll(this World world) {
			// Snapshot all entity IDs before any destruction to avoid mutation during iteration.
			var toDestroy = new List<int>();
			foreach (Archetype arch in world.GetMatchingArchetypes(Array.Empty<int>(), null)) {
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++)
					toDestroy.Add(entities[i]);
			}
			foreach (int entity in toDestroy)
				world.Destroy(entity);
		}
	}
}
