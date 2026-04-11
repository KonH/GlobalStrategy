using System.Collections.Generic;

namespace ECS.Viewer {
	public class WorldSnapshot {
		public List<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
	}

	public class EntitySnapshot {
		public int Id { get; set; }
		public List<ComponentSnapshot> Components { get; set; } = new List<ComponentSnapshot>();
	}

	public class ComponentSnapshot {
		public string TypeName { get; set; } = string.Empty;
		// Values are primitives, strings, enums (as string), or EntityRefValue for EntityRef fields.
		public Dictionary<string, object?> Fields { get; set; } = new Dictionary<string, object?>();
	}

	// Sentinel used in Fields dict to mark an EntityRef field.
	public class EntityRefValue {
		public int EntityId { get; set; }
		public EntityRefValue(int id) => EntityId = id;
	}
}
