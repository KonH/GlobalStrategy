using System;
using System.Reflection;
using ECS.Viewer;
using GS.Game.Common;

namespace ECS.Viewer.Host {
	public static class SnapshotFieldMetadata {
		public static bool Apply(Type componentType, MemberInfo member, FieldSnapshot field) {
			if (member.GetCustomAttribute<OmitFromSnapshotAttribute>() != null) {
				return false;
			}

			var suggestion = member.GetCustomAttribute<ParamSuggestionAttribute>();
			if (suggestion == null) {
				return true;
			}

			string typeName = suggestion.GetType().Name;
			field.DomainIdKind = typeName.EndsWith("Attribute", StringComparison.Ordinal)
				? typeName.Substring(0, typeName.Length - "Attribute".Length)
				: typeName;
			field.AllowEmpty = suggestion.AllowEmpty;
			if (suggestion is OwnerIdAttribute owner) {
				field.OwnerTypeSibling = owner.OwnerTypeSibling;
			}
			return true;
		}
	}
}
