using System;
using System.Collections.Generic;
using System.Reflection;

namespace ECS.Viewer {
	public class WorldObserver {
		public WorldSnapshot Capture(World world) {
			var snapshot = new WorldSnapshot();
			foreach (Archetype arch in world.GetMatchingArchetypes(Array.Empty<int>(), null)) {
				int count = arch.Count;
				if (count == 0) {
					continue;
				}
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++) {
					int entityId = entities[i];
					var entitySnap = new EntitySnapshot { Id = entityId };
					foreach (int typeId in arch.GetColumnTypeIds()) {
						Array column = arch.GetColumnRaw(typeId);
						Type compType = column.GetType().GetElementType()!;
						object? value = column.GetValue(i);
						var compSnap = new ComponentSnapshot { TypeName = compType.Name };
						if (value != null) {
							CaptureFields(compSnap, value, compType);
						}
						entitySnap.Components.Add(compSnap);
					}
					snapshot.Entities.Add(entitySnap);
				}
			}
			return snapshot;
		}

		static void CaptureFields(ComponentSnapshot snap, object value, Type type) {
			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (FieldInfo field in fields) {
				object? fieldVal = field.GetValue(value);
				if (field.FieldType == typeof(EntityRef)) {
					snap.Fields[field.Name] = new EntityRefValue(((EntityRef)(fieldVal ?? default(EntityRef))).Id);
				} else {
					snap.Fields[field.Name] = fieldVal;
				}
			}
			// Also capture record struct properties (get-only, compiler-generated backing fields start with <Name>)
			PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			foreach (PropertyInfo prop in props) {
				if (!prop.CanRead || prop.GetIndexParameters().Length > 0) {
					continue;
				}
				if (snap.Fields.ContainsKey(prop.Name)) {
					continue;
				}
				object? propVal = prop.GetValue(value);
				if (prop.PropertyType == typeof(EntityRef)) {
					snap.Fields[prop.Name] = new EntityRefValue(((EntityRef)(propVal ?? default(EntityRef))).Id);
				} else {
					snap.Fields[prop.Name] = propVal;
				}
			}
		}

		/// <summary>
		/// Writes back primitive/enum fields. EntityRef fields are rejected (returns false).
		/// </summary>
		public bool TrySetField(World world, int entityId, string typeName, string fieldName, string rawValue) {
			if (!world.IsAlive(entityId)) {
				return false;
			}
			foreach (Archetype arch in world.GetMatchingArchetypes(Array.Empty<int>(), null)) {
				int count = arch.Count;
				int[] entities = arch.Entities;
				for (int i = 0; i < count; i++) {
					if (entities[i] != entityId) {
						continue;
					}
					foreach (int typeId in arch.GetColumnTypeIds()) {
						Array column = arch.GetColumnRaw(typeId);
						Type compType = column.GetType().GetElementType()!;
						if (compType.Name != typeName) {
							continue;
						}
						object? boxed = column.GetValue(i);
						if (boxed == null) {
							return false;
						}
						FieldInfo? field = compType.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
						if (field == null) {
							return false;
						}
						if (field.FieldType == typeof(EntityRef)) {
							return false; // read-only
						}
						object? converted = ConvertValue(field.FieldType, rawValue);
						if (converted == null) {
							return false;
						}
						field.SetValue(boxed, converted);
						column.SetValue(boxed, i);
						return true;
					}
				}
			}
			return false;
		}

		static object? ConvertValue(Type targetType, string raw) {
			if (targetType == typeof(int)) {
				return int.TryParse(raw, out int v) ? (object)v : null;
			}
			if (targetType == typeof(float)) {
				return float.TryParse(raw, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out float v) ? (object)v : null;
			}
			if (targetType == typeof(double)) {
				return double.TryParse(raw, System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out double v) ? (object)v : null;
			}
			if (targetType == typeof(bool)) {
				return bool.TryParse(raw, out bool v) ? (object)v : null;
			}
			if (targetType == typeof(string)) {
				return raw;
			}
			if (targetType.IsEnum) {
				try {
					return Enum.Parse(targetType, raw, ignoreCase: true);
				} catch {
					return null;
				}
			}
			return null;
		}
	}
}
