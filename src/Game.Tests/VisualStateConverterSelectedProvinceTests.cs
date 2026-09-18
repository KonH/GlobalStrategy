using GS.Game.Components;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterSelectedProvinceTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();

		static TestWorld SeedSelection(TestWorld world, string provinceId) {
			return world.Entity().With(new ProvinceSelection { ProvinceId = provinceId });
		}

		static TestWorld SeedProvinceResource(TestWorld world, string provinceId, string resourceId, double value) {
			return world.Resource(provinceId, resourceId, value, OwnerType.Province);
		}

		SelectedProvinceState ProjectSelectedProvince(TestWorld world) {
			var probe = new VisualStateProbe(resources: _resources, relations: _relations);
			probe.Converter.UpdateSelectedProvince(world);
			return probe.State.SelectedProvince;
		}

		[Fact]
		void selecting_province_populates_its_resources() {
			var world = TestWorld.Create();
			SeedSelection(world, "c_alpha__province_one");
			SeedProvinceResource(world, "c_alpha__province_one", "population", 42.0);

			SelectedProvinceState selected = ProjectSelectedProvince(world);

			Assert.True(selected.IsValid);
			Assert.Equal("c_alpha__province_one", selected.ProvinceId);
			Assert.Single(selected.Resources.Resources);
			Assert.Equal("population", selected.Resources.Resources[0].ResourceId);
			Assert.Equal(42.0, selected.Resources.Resources[0].Value.Actual);
		}

		[Fact]
		void deselecting_with_empty_province_id_clears_is_valid_and_resources() {
			var world = TestWorld.Create();
			SeedSelection(world, "");

			SelectedProvinceState selected = ProjectSelectedProvince(world);

			Assert.False(selected.IsValid);
			Assert.Equal("", selected.ProvinceId);
			Assert.Empty(selected.Resources.Resources);
		}

		[Fact]
		void resources_are_scoped_to_the_selected_province_only() {
			var world = TestWorld.Create();
			SeedSelection(world, "c_alpha__province_one");
			SeedProvinceResource(world, "c_alpha__province_one", "population", 42.0);
			SeedProvinceResource(world, "c_alpha__province_two", "population", 99.0);

			SelectedProvinceState selected = ProjectSelectedProvince(world);

			Assert.Single(selected.Resources.Resources);
			Assert.Equal(42.0, selected.Resources.Resources[0].Value.Actual);
		}

		[Fact]
		void no_province_selection_entity_at_all_leaves_state_invalid() {
			SelectedProvinceState selected = ProjectSelectedProvince(TestWorld.Create());

			Assert.False(selected.IsValid);
			Assert.Empty(selected.Resources.Resources);
		}
	}
}
