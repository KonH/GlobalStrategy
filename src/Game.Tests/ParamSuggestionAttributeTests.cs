using System;
using System.Linq;
using System.Reflection;
using GS.Game.Commands;
using GS.Game.Common;
using Xunit;

namespace GS.Game.Tests {
	public class ParamSuggestionAttributeTests {
		[Theory]
		[InlineData(typeof(CountryIdAttribute))]
		[InlineData(typeof(OrgIdAttribute))]
		[InlineData(typeof(ProvinceIdAttribute))]
		[InlineData(typeof(ActionIdAttribute))]
		[InlineData(typeof(RoleIdAttribute))]
		[InlineData(typeof(CharacterOwnerIdAttribute))]
		[InlineData(typeof(LocaleIdAttribute))]
		[InlineData(typeof(OneOfAttribute))]
		[InlineData(typeof(ResourceIdAttribute))]
		[InlineData(typeof(CharacterIdAttribute))]
		[InlineData(typeof(WarIdAttribute))]
		[InlineData(typeof(BattleIdAttribute))]
		[InlineData(typeof(EffectIdAttribute))]
		[InlineData(typeof(TaskIdAttribute))]
		[InlineData(typeof(CollectorIdAttribute))]
		[InlineData(typeof(OwnerIdAttribute))]
		public void SuggestionAttribute_TargetsFieldAndProperty(Type attributeType) {
			var usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();

			Assert.NotNull(usage);
			Assert.Equal(AttributeTargets.Field | AttributeTargets.Property, usage!.ValidOn);
		}

		[Theory]
		[InlineData(typeof(CountryIdAttribute))]
		[InlineData(typeof(OrgIdAttribute))]
		[InlineData(typeof(ProvinceIdAttribute))]
		[InlineData(typeof(ActionIdAttribute))]
		[InlineData(typeof(RoleIdAttribute))]
		[InlineData(typeof(CharacterOwnerIdAttribute))]
		[InlineData(typeof(LocaleIdAttribute))]
		[InlineData(typeof(OneOfAttribute))]
		[InlineData(typeof(ResourceIdAttribute))]
		[InlineData(typeof(CharacterIdAttribute))]
		[InlineData(typeof(WarIdAttribute))]
		[InlineData(typeof(BattleIdAttribute))]
		[InlineData(typeof(EffectIdAttribute))]
		[InlineData(typeof(TaskIdAttribute))]
		[InlineData(typeof(CollectorIdAttribute))]
		[InlineData(typeof(OwnerIdAttribute))]
		public void SuggestionAttribute_DerivesFromParamSuggestionAttribute(Type attributeType) {
			Assert.True(typeof(ParamSuggestionAttribute).IsAssignableFrom(attributeType));
		}

		[Fact]
		public void OneOfAttribute_RetainsConstructorValues() {
			var attribute = new OneOfAttribute("a", "b");

			Assert.Equal(new[] { "a", "b" }, attribute.Values);
		}

		[Fact]
		public void ParamSuggestionAttribute_AllowEmpty_DefaultsFalse() {
			Assert.False(new CountryIdAttribute().AllowEmpty);
		}

		[Fact]
		public void OwnerIdAttribute_OwnerTypeSibling_DefaultsToOwnerType() {
			Assert.Equal("OwnerType", new OwnerIdAttribute().OwnerTypeSibling);
		}

		[Fact]
		public void OwnerIdAttribute_OwnerTypeSibling_CanBeNull() {
			Assert.Null(new OwnerIdAttribute { OwnerTypeSibling = null }.OwnerTypeSibling);
		}

		[Fact]
		public void OmitFromSnapshotAttribute_TargetsFieldAndProperty() {
			var usage = typeof(OmitFromSnapshotAttribute).GetCustomAttribute<AttributeUsageAttribute>();

			Assert.NotNull(usage);
			Assert.Equal(AttributeTargets.Field | AttributeTargets.Property, usage!.ValidOn);
			Assert.False(typeof(ParamSuggestionAttribute).IsAssignableFrom(typeof(OmitFromSnapshotAttribute)));
		}

		// Every public string field/property named "*Id" (plus the known "Locale"/"Interval"
		// exceptions) on a discovered ICommand type must carry a ParamSuggestionAttribute -
		// this is the same closed set src/Game.SourceGenerators/CommandGenerator.cs enumerates,
		// so a forgotten annotation on a new command fails this test at build time rather than
		// silently offering no terminal auto-complete suggestions.
		[Fact]
		public void EveryCommand_DomainIdMember_CarriesSuggestionAttribute() {
			var commandTypes = typeof(ICommand).Assembly.GetTypes()
				.Where(t => typeof(ICommand).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

			var missing = new System.Collections.Generic.List<string>();

			foreach (var type in commandTypes) {
				foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
					if (!IsDomainIdMember(field.Name)) {
						continue;
					}
					if (!HasSuggestionAttribute(field.GetCustomAttributes(inherit: false))) {
						missing.Add($"{type.Name}.{field.Name} (field)");
					}
				}

				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
					if (!IsDomainIdMember(property.Name)) {
						continue;
					}
					if (!HasSuggestionAttribute(property.GetCustomAttributes(inherit: false))) {
						missing.Add($"{type.Name}.{property.Name} (property)");
					}
				}
			}

			Assert.True(missing.Count == 0, $"Missing ParamSuggestionAttribute on: {string.Join(", ", missing)}");
		}

		static bool IsDomainIdMember(string memberName) {
			return memberName.EndsWith("Id", StringComparison.Ordinal)
				|| memberName == "Locale"
				|| memberName == "Interval";
		}

		static bool HasSuggestionAttribute(object[] attributes) {
			return attributes.Any(a => a is ParamSuggestionAttribute);
		}
	}
}
