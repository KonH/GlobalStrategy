using System;

namespace GS.Game.Common {
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public abstract class ParamSuggestionAttribute : Attribute {
		public bool AllowEmpty { get; set; }
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class CountryIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class OrgIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class ProvinceIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class ActionIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class RoleIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class CharacterOwnerIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class LocaleIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class ResourceIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class CharacterIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class WarIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class BattleIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class EffectIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class TaskIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class CollectorIdAttribute : ParamSuggestionAttribute { }

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class OwnerIdAttribute : ParamSuggestionAttribute {
		public string? OwnerTypeSibling { get; set; } = "OwnerType";
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class OneOfAttribute : ParamSuggestionAttribute {
		public string[] Values { get; }

		public OneOfAttribute(params string[] values) {
			Values = values;
		}
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public sealed class OmitFromSnapshotAttribute : Attribute { }
}
