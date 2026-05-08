using System;
using System.Collections.Generic;

namespace GS.Main {
	public class WorldSnapshot {
		public SaveHeader Header { get; set; } = new SaveHeader();
		public List<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
	}

	public class SaveHeader {
		public string SaveName { get; set; } = "";
		public string OrganizationId { get; set; } = "";
		public DateTime GameDate { get; set; }
		public DateTime SavedAt { get; set; }
	}

	public class EntitySnapshot {
		// Key: component type full name (e.g. "GS.Game.Components.Country")
		// Value: field/property name → serialized string value
		public Dictionary<string, Dictionary<string, string?>> Components { get; set; } =
			new Dictionary<string, Dictionary<string, string?>>();
	}
}
