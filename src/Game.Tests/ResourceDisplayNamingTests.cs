using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceDisplayNamingTests {
		[Fact]
		void name_key_derives_from_resource_id() {
			Assert.Equal("resource.country_population.name", ResourceDisplayNaming.NameKey("country_population"));
		}

		[Fact]
		void description_key_derives_from_resource_id() {
			Assert.Equal("resource.country_population.description", ResourceDisplayNaming.DescriptionKey("country_population"));
		}

		[Fact]
		void icon_class_derives_from_resource_id() {
			Assert.Equal("resource-icon--country_population", ResourceDisplayNaming.IconClass("country_population"));
		}

		[Fact]
		void effect_name_key_derives_from_effect_id() {
			Assert.Equal("effect.base_income.name", ResourceDisplayNaming.EffectNameKey("base_income"));
		}

		[Fact]
		void effect_description_key_derives_from_effect_id() {
			Assert.Equal("effect.base_income.description", ResourceDisplayNaming.EffectDescriptionKey("base_income"));
		}

		[Fact]
		void underscore_survives_verbatim_with_no_separator_transformation() {
			Assert.Equal("resource.country_population.name", ResourceDisplayNaming.NameKey("country_population"));
			Assert.DoesNotContain("-population", ResourceDisplayNaming.NameKey("country_population"));
			Assert.Equal("resource-icon--country_population", ResourceDisplayNaming.IconClass("country_population"));
			Assert.DoesNotContain("--country-population", ResourceDisplayNaming.IconClass("country_population"));
		}
	}
}
