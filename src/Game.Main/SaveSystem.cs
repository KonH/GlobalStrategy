using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ECS;
using GS.Game.Components;

namespace GS.Main {
	public static class SaveSystem {
		sealed class MemberAccessor {
		public string Name { get; }
		public Func<object, object?> GetValue { get; }
		public Type ValueType { get; }
		public MemberAccessor(string name, Func<object, object?> getValue, Type valueType) {
			Name = name;
			GetValue = getValue;
			ValueType = valueType;
		}
	}

		static readonly Type[] _savableTypes;
		static readonly Dictionary<int, (Type Type, MemberAccessor[] Members)> _typeMap;

		static SaveSystem() {
			var componentAssembly = typeof(SavableAttribute).Assembly;
			_savableTypes = componentAssembly.GetTypes()
				.Where(t => t.GetCustomAttribute<SavableAttribute>() != null)
				.ToArray();

			_typeMap = new Dictionary<int, (Type, MemberAccessor[])>();
			foreach (var type in _savableTypes) {
				int typeId = (int)typeof(TypeId<>).MakeGenericType(type)
					.GetField("Value")!.GetValue(null)!;
				var members = BuildMembers(type);
				_typeMap[typeId] = (type, members);
			}
		}

		static MemberAccessor[] BuildMembers(Type type) {
			var result = new List<MemberAccessor>();
			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
				var f = field;
				result.Add(new MemberAccessor(f.Name, obj => f.GetValue(obj), f.FieldType));
			}
			foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (!prop.CanRead || prop.GetMethod == null) {
					continue;
				}
				var p = prop;
				result.Add(new MemberAccessor(p.Name, obj => p.GetValue(obj), p.PropertyType));
			}
			return result.ToArray();
		}

		public static WorldSnapshot BuildSnapshot(World world) {
			var entities = new List<EntitySnapshot>();

			foreach (var arch in world.GetMatchingArchetypes(Array.Empty<int>(), null)) {
				var savableColumns = new List<(Type Type, MemberAccessor[] Members, Array Column)>();
				foreach (int typeId in arch.GetColumnTypeIds()) {
					if (_typeMap.TryGetValue(typeId, out var info)) {
						savableColumns.Add((info.Type, info.Members, arch.GetColumnRaw(typeId)));
					}
				}
				if (savableColumns.Count == 0) {
					continue;
				}

				int count = arch.Count;
				for (int row = 0; row < count; row++) {
					var components = new Dictionary<string, Dictionary<string, string?>>();
					foreach (var (type, members, column) in savableColumns) {
						object compValue = column.GetValue(row)!;
						var fieldValues = new Dictionary<string, string?>();
						foreach (var member in members) {
							fieldValues[member.Name] = SerializeValue(member.GetValue(compValue));
						}
						components[type.FullName!] = fieldValues;
					}
					entities.Add(new EntitySnapshot { Components = components });
				}
			}

			string playerCountryId = "";
			DateTime gameDate = default;

			int[] playerRequired = { TypeId<Country>.Value, TypeId<Player>.Value };
			foreach (var arch in world.GetMatchingArchetypes(playerRequired, null)) {
				if (arch.Count > 0) {
					playerCountryId = arch.GetColumn<Country>()[0].CountryId;
					break;
				}
			}

			int[] timeRequired = { TypeId<GameTime>.Value };
			foreach (var arch in world.GetMatchingArchetypes(timeRequired, null)) {
				if (arch.Count > 0) {
					gameDate = arch.GetColumn<GameTime>()[0].CurrentTime;
					break;
				}
			}

			string saveName = $"{playerCountryId}_{gameDate:yyyy-MM-dd}";

			return new WorldSnapshot {
				Header = new SaveHeader {
					SaveName = saveName,
					PlayerCountryId = playerCountryId,
					GameDate = gameDate,
					SavedAt = DateTime.UtcNow
				},
				Entities = entities
			};
		}

		static string? SerializeValue(object? value) {
			if (value == null) {
				return null;
			}
			if (value is DateTime dt) {
				return dt.ToString("O", CultureInfo.InvariantCulture);
			}
			if (value is Enum) {
				return Convert.ToInt32(value).ToString(CultureInfo.InvariantCulture);
			}
			if (value is float f) {
				return f.ToString("R", CultureInfo.InvariantCulture);
			}
			if (value is double d) {
				return d.ToString("R", CultureInfo.InvariantCulture);
			}
			return value.ToString();
		}
	}
}
