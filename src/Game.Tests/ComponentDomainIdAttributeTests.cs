using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GS.Game.Common;
using GS.Game.Components;
using Xunit;

namespace GS.Game.Tests {
	public class ComponentDomainIdAttributeTests {
		[Fact]
		public void EveryComponent_DomainIdMember_CarriesSuggestionAttribute() {
			var componentTypes = typeof(Country).Assembly.GetTypes()
				.Where(t => t.IsValueType && t.IsPublic && !t.IsEnum && !t.IsPrimitive);

			var missing = new List<string>();

			foreach (var type in componentTypes) {
				foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
					if (field.FieldType != typeof(string) || !IsDomainIdMember(type, field.Name)) {
						continue;
					}
					if (!HasSuggestionAttribute(field.GetCustomAttributes(inherit: false))) {
						missing.Add($"{type.Name}.{field.Name} (field)");
					}
				}

				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
					if (property.PropertyType != typeof(string) || !IsDomainIdMember(type, property.Name)) {
						continue;
					}
					if (!HasSuggestionAttribute(property.GetCustomAttributes(inherit: false))) {
						missing.Add($"{type.Name}.{property.Name} (property)");
					}
				}
			}

			Assert.True(missing.Count == 0, $"Missing ParamSuggestionAttribute on: {string.Join(", ", missing)}");
		}

		static bool IsDomainIdMember(Type declaringType, string memberName) {
			if (memberName.EndsWith("Id", StringComparison.Ordinal) || memberName == "Locale") {
				return true;
			}
			if (memberName == "Value" && (declaringType == typeof(TaskId) || declaringType == typeof(Locale))) {
				return true;
			}
			return false;
		}

		static bool HasSuggestionAttribute(object[] attributes) {
			return attributes.Any(a => a is ParamSuggestionAttribute);
		}
	}
}
