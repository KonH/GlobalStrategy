using System.Collections.Generic;

namespace ECS {
	public interface IReadOnlyWorld {
		bool IsAlive(int entity);
		bool Has<TComp>(int entity);
		ref TComp Get<TComp>(int entity);
		bool TryGet<TComp>(int entity, out TComp comp);
		IEnumerable<Archetype> GetMatchingArchetypes(int[] required, int[]? excluded);
	}
}
