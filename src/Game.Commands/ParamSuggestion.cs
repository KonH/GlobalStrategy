using System;

namespace GS.Game.Commands {
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public abstract class ParamSuggestionAttribute : Attribute { }

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
	public sealed class OneOfAttribute : ParamSuggestionAttribute {
		public string[] Values { get; }

		public OneOfAttribute(params string[] values) {
			Values = values;
		}
	}
}
