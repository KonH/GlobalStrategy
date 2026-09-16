using System.Collections.Generic;

namespace ECS.Viewer {
	public enum FieldSnapshotKind {
		Enum,
		Number,
		Bool,
		String
	}

	public class WorldSnapshot {
		public List<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
	}

	public class EntitySnapshot {
		public int Id { get; set; }
		public List<ComponentSnapshot> Components { get; set; } = new List<ComponentSnapshot>();
	}

	public class ComponentSnapshot {
		public string TypeName { get; set; } = string.Empty;
		public List<FieldSnapshot> Fields { get; set; } = new List<FieldSnapshot>();
	}

	public class FieldSnapshot {
		public string Name { get; set; } = string.Empty;
		public FieldSnapshotKind Kind { get; set; }
		public object? Value { get; set; }
		public string[]? EnumNames { get; set; }
		public string? DomainIdKind { get; set; }
		public bool? AllowEmpty { get; set; }
		public string? OwnerTypeSibling { get; set; }
	}

	// Sentinel used as FieldSnapshot.Value to mark an EntityRef field.
	public class EntityRefValue {
		public int EntityId { get; set; }
		public EntityRefValue(int id) => EntityId = id;
	}

	public delegate bool CaptureFieldCallback(System.Type componentType, System.Reflection.MemberInfo member, FieldSnapshot field);
}
