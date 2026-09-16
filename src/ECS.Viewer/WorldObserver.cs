using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace ECS.Viewer {
	public class WorldObserver {
		public WorldSnapshot Capture(World world, CaptureFieldCallback? fieldCallback = null) {
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
							CaptureFields(compSnap, value, compType, fieldCallback);
						}
						entitySnap.Components.Add(compSnap);
					}
					snapshot.Entities.Add(entitySnap);
				}
			}
			return snapshot;
		}

		static void CaptureFields(ComponentSnapshot snap, object value, Type type, CaptureFieldCallback? fieldCallback) {
			FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
			foreach (FieldInfo field in fields) {
				TryAddField(snap, type, field, field.FieldType, field.GetValue(value), fieldCallback);
			}
			PropertyInfo[] props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			foreach (PropertyInfo prop in props) {
				if (!prop.CanRead || prop.GetIndexParameters().Length > 0) {
					continue;
				}
				if (snap.Fields.Exists(f => f.Name == prop.Name)) {
					continue;
				}
				TryAddField(snap, type, prop, prop.PropertyType, prop.GetValue(value), fieldCallback);
			}
		}

		static void TryAddField(
			ComponentSnapshot snap,
			Type componentType,
			MemberInfo member,
			Type memberType,
			object? memberValue,
			CaptureFieldCallback? fieldCallback
		) {
			if (memberType == typeof(EntityRef)) {
				var field = new FieldSnapshot {
					Name = member.Name,
					Kind = FieldSnapshotKind.Number,
					Value = new EntityRefValue(((EntityRef)(memberValue ?? default(EntityRef))).Id)
				};
				if (fieldCallback != null && !fieldCallback(componentType, member, field)) {
					return;
				}
				snap.Fields.Add(field);
				return;
			}

			if (!TryCreateScalarSnapshot(member.Name, memberType, memberValue, out FieldSnapshot snapshot)) {
				return;
			}
			if (fieldCallback != null && !fieldCallback(componentType, member, snapshot)) {
				return;
			}
			snap.Fields.Add(snapshot);
		}

		static bool TryCreateScalarSnapshot(string name, Type memberType, object? memberValue, out FieldSnapshot snapshot) {
			snapshot = new FieldSnapshot { Name = name };
			if (memberType == typeof(string)) {
				snapshot.Kind = FieldSnapshotKind.String;
				snapshot.Value = memberValue;
				return true;
			}
			if (memberType == typeof(bool)) {
				snapshot.Kind = FieldSnapshotKind.Bool;
				snapshot.Value = memberValue;
				return true;
			}
			if (memberType == typeof(DateTime)) {
				snapshot.Kind = FieldSnapshotKind.String;
				snapshot.Value = ((DateTime)(memberValue ?? default(DateTime))).ToString("O", CultureInfo.InvariantCulture);
				return true;
			}
			if (memberType.IsEnum) {
				snapshot.Kind = FieldSnapshotKind.Enum;
				snapshot.Value = memberValue == null ? null : Enum.GetName(memberType, memberValue);
				snapshot.EnumNames = Enum.GetNames(memberType);
				return true;
			}
			if (IsNumeric(memberType)) {
				snapshot.Kind = FieldSnapshotKind.Number;
				snapshot.Value = memberValue;
				return true;
			}
			if (memberType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(memberType)) {
				return false;
			}
			if (memberType.IsClass || (memberType.IsValueType && !memberType.IsPrimitive && !memberType.IsEnum)) {
				return false;
			}
			return false;
		}

		static bool IsNumeric(Type type) {
			return type == typeof(byte) || type == typeof(sbyte)
				|| type == typeof(short) || type == typeof(ushort)
				|| type == typeof(int) || type == typeof(uint)
				|| type == typeof(long) || type == typeof(ulong)
				|| type == typeof(float) || type == typeof(double)
				|| type == typeof(decimal);
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
				return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? (object)v : null;
			}
			if (targetType == typeof(double)) {
				return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? (object)v : null;
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
