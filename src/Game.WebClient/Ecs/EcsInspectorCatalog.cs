using System;
using System.Collections.Generic;
using ECS.Viewer;
using EcsWorldSnapshot = ECS.Viewer.WorldSnapshot;

namespace GS.Game.WebClient.Ecs {
	public static class EcsInspectorCatalog {
		public static IReadOnlyList<string> TypeNames(EcsWorldSnapshot? snapshot) {
			var names = new SortedSet<string>(StringComparer.Ordinal);
			if (snapshot?.Entities == null) {
				return Array.Empty<string>();
			}

			foreach (EntitySnapshot entity in snapshot.Entities) {
				if (entity.Components == null) {
					continue;
				}
				foreach (ComponentSnapshot component in entity.Components) {
					if (!string.IsNullOrEmpty(component.TypeName)) {
						names.Add(component.TypeName);
					}
				}
			}

			return new List<string>(names);
		}

		public static IReadOnlyList<FieldSnapshot> FieldsFor(EcsWorldSnapshot? snapshot, string typeName) {
			if (snapshot?.Entities == null || string.IsNullOrEmpty(typeName)) {
				return Array.Empty<FieldSnapshot>();
			}

			var result = new List<FieldSnapshot>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (EntitySnapshot entity in snapshot.Entities) {
				if (entity.Components == null) {
					continue;
				}
				foreach (ComponentSnapshot component in entity.Components) {
					if (!string.Equals(component.TypeName, typeName, StringComparison.Ordinal) || component.Fields == null) {
						continue;
					}
					foreach (FieldSnapshot field in component.Fields) {
						if (seen.Add(field.Name)) {
							result.Add(field);
						}
					}
				}
			}

			return result;
		}

		public static string ComponentTags(EntitySnapshot entity) {
			if (entity.Components == null || entity.Components.Count == 0) {
				return "";
			}

			var names = new List<string>(entity.Components.Count);
			foreach (ComponentSnapshot component in entity.Components) {
				if (!string.IsNullOrEmpty(component.TypeName)) {
					names.Add(component.TypeName);
				}
			}
			return string.Join(", ", names);
		}

		public static IReadOnlyList<string> OrderedTypeNames(
			IReadOnlyList<string> typeNames,
			IReadOnlyDictionary<string, ComponentFilterMode> chips,
			IReadOnlyList<string> activeOrder
		) {
			var result = new List<string>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			if (activeOrder != null) {
				foreach (string name in activeOrder) {
					if (string.IsNullOrEmpty(name) || !seen.Add(name)) {
						continue;
					}
					if (chips != null && chips.TryGetValue(name, out ComponentFilterMode mode) && mode != ComponentFilterMode.Off) {
						result.Add(name);
					}
				}
			}

			if (typeNames == null) {
				return result;
			}

			foreach (string name in typeNames) {
				if (seen.Contains(name)) {
					continue;
				}
				if (chips != null && chips.TryGetValue(name, out ComponentFilterMode mode) && mode != ComponentFilterMode.Off) {
					seen.Add(name);
					result.Add(name);
				}
			}

			foreach (string name in typeNames) {
				if (seen.Add(name)) {
					result.Add(name);
				}
			}

			return result;
		}
	}
}
