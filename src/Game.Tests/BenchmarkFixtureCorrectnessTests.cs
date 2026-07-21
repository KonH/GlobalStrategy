using System;
using System.IO;
using ECS;
using GS.Configs.IO;
using GS.Game.Benchmarks;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	// BenchmarkDotNet itself only measures elapsed time - a silently-broken no-op benchmark
	// still reports a fast, misleading number. These tests prove that the exact call shape
	// each corresponding benchmark method uses actually exercises real, non-degenerate work.
	public class BenchmarkFixtureCorrectnessTests {
		sealed class TrackedStubCollector : IResourceCollector {
			public bool WasInvoked;
			readonly double _delta;
			public TrackedStubCollector(double delta) { _delta = delta; }

			public double Compute(string ownerId, double currentValue, IReadOnlyWorld world) {
				WasInvoked = true;
				return _delta;
			}
		}

		static World BuildMonthlyCollectorWorld(out int resourceEntity, out TrackedStubCollector stub, out ResourceCollectorRegistry registry) {
			var world = new World();
			resourceEntity = world.Create();
			world.Add(resourceEntity, new ResourceOwner("Russia"));
			world.Add(resourceEntity, new Resource { ResourceId = ResourceDefinitions.CountryPopulation, Value = 100.0 });

			int effectEntity = world.Create();
			world.Add(effectEntity, new ResourceOwner("Russia"));
			world.Add(effectEntity, new ResourceLink(ResourceDefinitions.CountryPopulation));
			world.Add(effectEntity, new ResourceEffect { EffectId = "stub_effect", Value = 0.0, PayType = PayType.Monthly });
			world.Add(effectEntity, new ResourceCollector { CollectorId = "stub" });

			stub = new TrackedStubCollector(10.0);
			registry = new ResourceCollectorRegistry();
			registry.Register("stub", stub);
			return world;
		}

		[Fact]
		void resource_system_month_boundary_call_shape_actually_triggers_collector_resolve() {
			var world = BuildMonthlyCollectorWorld(out int resourceEntity, out var stub, out var registry);

			ResourceSystem.Update(
				world, ResourceSystemBenchmarks.BoundaryPrevious, ResourceSystemBenchmarks.BoundaryCurrent,
				registry, new[] { ResourceDefinitions.CountryPopulation });

			Assert.True(stub.WasInvoked);
			Assert.Equal(110.0, world.Get<Resource>(resourceEntity).Value);
		}

		[Fact]
		void resource_system_regular_day_call_shape_does_not_trigger_monthly_collectors() {
			var world = BuildMonthlyCollectorWorld(out int resourceEntity, out var stub, out var registry);

			ResourceSystem.Update(
				world, ResourceSystemBenchmarks.RegularPrevious, ResourceSystemBenchmarks.RegularCurrent,
				registry, new[] { ResourceDefinitions.CountryPopulation });

			Assert.False(stub.WasInvoked);
			Assert.Equal(100.0, world.Get<Resource>(resourceEntity).Value);
		}

		// dotnet test runs from the test assembly's own output directory, not the repo root -
		// walk up until Assets/Configs is found (same problem GameWorldFixture solves for
		// BenchmarkDotNet's out-of-process subprocess, via a different mechanism since there is
		// no equivalent "parent process sets an env var" here).
		static string FindRepoRootConfigPath(string fileName) {
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null) {
				string candidate = Path.Combine(dir.FullName, "Assets", "Configs", fileName);
				if (File.Exists(candidate)) { return candidate; }
				dir = dir.Parent;
			}
			throw new InvalidOperationException($"Could not locate Assets/Configs/{fileName} above {AppContext.BaseDirectory}.");
		}

		[Fact]
		void country_population_collector_benchmark_country_owns_at_least_one_province() {
			var provinceConfig = new FileConfig<ProvinceConfig>(FindRepoRootConfigPath("province_config.json")).Load();
			Assert.True(provinceConfig.Provinces.Count > 0, "province_config.json has no provinces - cannot pick a fixture country.");

			// Mirrors GameWorldFixture.Build()'s own FirstCountryId derivation exactly.
			string countryId = provinceConfig.Provinces[0].CountryId;

			var owned = provinceConfig.FindByCountryId(countryId);
			Assert.True(owned.Count >= 1,
				$"Fixture country '{countryId}' owns zero provinces in the committed config - " +
				"CountryPopulationCollectorBenchmarks would measure an empty loop.");
		}

		[Fact]
		void full_tick_iteration_setup_reliably_forces_month_boundary() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new GameTime { CurrentTime = FullTickBenchmarks.MonthBoundaryDate, AccumulatedHours = 0f, IsPaused = false, MultiplierIndex = 0 });

			TimeSystem.Update(world, entity, 24f, new[] { 1, 24, 720 },
				default, default, default);

			DateTime result = world.Get<GameTime>(entity).CurrentTime;
			Assert.NotEqual(FullTickBenchmarks.MonthBoundaryDate.Month, result.Month);
		}

		[Fact]
		void full_tick_iteration_setup_reliably_avoids_month_boundary() {
			var world = new World();
			int entity = world.Create();
			world.Add(entity, new GameTime { CurrentTime = FullTickBenchmarks.RegularDayDate, AccumulatedHours = 0f, IsPaused = false, MultiplierIndex = 0 });

			TimeSystem.Update(world, entity, 24f, new[] { 1, 24, 720 },
				default, default, default);

			DateTime result = world.Get<GameTime>(entity).CurrentTime;
			Assert.Equal(FullTickBenchmarks.RegularDayDate.Month, result.Month);
		}
	}
}
