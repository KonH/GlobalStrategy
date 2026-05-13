using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ECS;
using ECS.Extensions;
using GS.Game.Components;

namespace GS.Main {
	public static class LoadSystem {
		static readonly Dictionary<string, (Type Type, MemberSetter[] Members)> _typeMap;
		static readonly MethodInfo _worldAddMethod;

		sealed class MemberSetter {
		public string Name { get; }
		public Action<object, object?> SetValue { get; }
		public Type ValueType { get; }
		public MemberSetter(string name, Action<object, object?> setValue, Type valueType) {
			Name = name;
			SetValue = setValue;
			ValueType = valueType;
		}
	}

		static LoadSystem() {
			var componentAssembly = typeof(SavableAttribute).Assembly;
			var savableTypes = componentAssembly.GetTypes()
				.Where(t => t.GetCustomAttribute<SavableAttribute>() != null);

			_typeMap = new Dictionary<string, (Type, MemberSetter[])>();
			foreach (var type in savableTypes) {
				var setters = BuildSetters(type);
				_typeMap[type.FullName!] = (type, setters);
			}

			_worldAddMethod = typeof(World).GetMethod("Add")!;
		}

		static MemberSetter[] BuildSetters(Type type) {
			var result = new List<MemberSetter>();
			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
				var f = field;
				result.Add(new MemberSetter(f.Name, (obj, val) => f.SetValue(obj, val), f.FieldType));
			}
			foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (!prop.CanWrite || prop.SetMethod == null) {
					continue;
				}
				var p = prop;
				result.Add(new MemberSetter(p.Name, (obj, val) => p.SetValue(obj, val), p.PropertyType));
			}
			return result.ToArray();
		}

		public static void Apply(WorldSnapshot snapshot, World world) {
			world.DestroyAll();

			foreach (var entitySnapshot in snapshot.Entities) {
				int entity = world.Create();
				foreach (var (typeName, fieldValues) in entitySnapshot.Components) {
					if (!_typeMap.TryGetValue(typeName, out var info)) {
						continue;
					}
					object component = Activator.CreateInstance(info.Type)!;
					foreach (var setter in info.Members) {
						if (!fieldValues.TryGetValue(setter.Name, out var rawValue)) {
							continue;
						}
						object? converted = DeserializeValue(rawValue, setter.ValueType);
						setter.SetValue(component, converted);
					}
					_worldAddMethod.MakeGenericMethod(info.Type).Invoke(world, new[] { entity, component });
				}
			}
		}

		static object? DeserializeValue(string? value, Type targetType) {
			if (value == null) {
				return null;
			}
			if (targetType == typeof(string[])) {
				return value.Length == 0 ? Array.Empty<string>() : value.Split('\x1F');
			}
			if (targetType == typeof(string)) {
				return value;
			}
			if (targetType == typeof(DateTime)) {
				return DateTime.ParseExact(value, "O", CultureInfo.InvariantCulture);
			}
			if (targetType.IsEnum) {
				return Enum.ToObject(targetType, int.Parse(value, CultureInfo.InvariantCulture));
			}
			if (targetType == typeof(bool)) {
				return bool.Parse(value);
			}
			if (targetType == typeof(int)) {
				return int.Parse(value, CultureInfo.InvariantCulture);
			}
			if (targetType == typeof(float)) {
				return float.Parse(value, CultureInfo.InvariantCulture);
			}
			if (targetType == typeof(double)) {
				return double.Parse(value, CultureInfo.InvariantCulture);
			}
			return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
		}
	}
}
