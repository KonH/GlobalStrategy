using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterSelectedProvinceTests {
		static int SeedSelection(World world, string provinceId) {
			int entity = world.Create();
			world.Add(entity, new ProvinceSelection { ProvinceId = provinceId });
			return entity;
		}

		static int SeedProvinceResource(World world, string provinceId, string resourceId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(provinceId, OwnerType.Province));
			world.Add(entity, new Resource { ResourceId = resourceId, Value = value });
			return entity;
		}

		[Fact]
		void selecting_province_populates_its_resources() {
			var world = new World();
			SeedSelection(world, "c_alpha__province_one");
			SeedProvinceResource(world, "c_alpha__province_one", "population", 42.0);

			var state = new VisualState();
			var converter = new VisualStateConverter(state);
			converter.UpdateSelectedProvince(world);

			Assert.True(state.SelectedProvince.IsValid);
			Assert.Equal("c_alpha__province_one", state.SelectedProvince.ProvinceId);
			Assert.Single(state.SelectedProvince.Resources.Resources);
			Assert.Equal("population", state.SelectedProvince.Resources.Resources[0].ResourceId);
			Assert.Equal(42.0, state.SelectedProvince.Resources.Resources[0].Value.Actual);
		}

		[Fact]
		void deselecting_with_empty_province_id_clears_is_valid_and_resources() {
			var world = new World();
			SeedSelection(world, "");

			var state = new VisualState();
			var converter = new VisualStateConverter(state);
			converter.UpdateSelectedProvince(world);

			Assert.False(state.SelectedProvince.IsValid);
			Assert.Equal("", state.SelectedProvince.ProvinceId);
			Assert.Empty(state.SelectedProvince.Resources.Resources);
		}

		[Fact]
		void resources_are_scoped_to_the_selected_province_only() {
			var world = new World();
			SeedSelection(world, "c_alpha__province_one");
			SeedProvinceResource(world, "c_alpha__province_one", "population", 42.0);
			SeedProvinceResource(world, "c_alpha__province_two", "population", 99.0);

			var state = new VisualState();
			var converter = new VisualStateConverter(state);
			converter.UpdateSelectedProvince(world);

			Assert.Single(state.SelectedProvince.Resources.Resources);
			Assert.Equal(42.0, state.SelectedProvince.Resources.Resources[0].Value.Actual);
		}

		[Fact]
		void no_province_selection_entity_at_all_leaves_state_invalid() {
			var world = new World();

			var state = new VisualState();
			var converter = new VisualStateConverter(state);
			converter.UpdateSelectedProvince(world);

			Assert.False(state.SelectedProvince.IsValid);
			Assert.Empty(state.SelectedProvince.Resources.Resources);
		}
	}
}
