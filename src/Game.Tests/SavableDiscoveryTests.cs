using System;
using System.Linq;
using System.Reflection;
using GS.Game.Components;
using Xunit;

namespace GS.Game.Tests {
	public class SavableDiscoveryTests {
		static readonly Type[] ExpectedSavable = {
			typeof(Country),
			typeof(IsSelected),
			typeof(Player),
			typeof(GameTime),
			typeof(Locale),
			typeof(AppSettings),
			typeof(Resource),
			typeof(ResourceOwner),
			typeof(ResourceLink),
			typeof(ResourceEffect)
		};

		static readonly Type[] ExpectedNotSavable = {
			typeof(TriggerSave)
		};

		[Fact]
		void all_expected_persistent_components_have_savable_attribute() {
			foreach (var type in ExpectedSavable) {
				var attr = type.GetCustomAttribute<SavableAttribute>();
				Assert.True(attr != null, $"{type.Name} is missing [Savable] attribute");
			}
		}

		[Fact]
		void ephemeral_components_do_not_have_savable_attribute() {
			foreach (var type in ExpectedNotSavable) {
				var attr = type.GetCustomAttribute<SavableAttribute>();
				Assert.True(attr == null, $"{type.Name} should NOT have [Savable] attribute");
			}
		}
	}
}
