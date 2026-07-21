using System;
using System.Collections.Generic;
using System.IO;
using ECS;
using GS.Configs.IO;
using GS.Game.Components;
using GS.Game.ConsoleRunner;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;

namespace GS.Game.Benchmarks {
	// Builds the real, fully-populated 163-country/province/org world from the committed
	// Assets/Configs data - never a hand-built minimal fixture. Each benchmark class calls
	// Build() independently in its own [GlobalSetup]; cost is excluded from measurement.
	public static class GameWorldFixture {
		// Set by Program.Main (parent process) before invoking BenchmarkSwitcher, and read back
		// here in whatever process actually runs [GlobalSetup] - the parent's own working
		// directory (repo root) does not carry over to BenchmarkDotNet's out-of-process
		// toolchain's spawned subprocess, whose working directory is a build-output subfolder.
		public const string RepoRootEnvVar = "GAME_BENCHMARKS_REPO_ROOT";
		public const int Seed = 1880;

		public static string ConfigDir {
			get {
				string? repoRoot = Environment.GetEnvironmentVariable(RepoRootEnvVar);
				return repoRoot != null ? Path.Combine(repoRoot, "Assets", "Configs") : "Assets/Configs";
			}
		}

		public readonly struct Fixture {
			public readonly GameLogic Logic;
			public readonly int GameTimeEntity;
			public readonly ResourceCollectorRegistry CollectorRegistry;
			public readonly IReadOnlyList<string> ResourceIdUpdateOrder;
			public readonly string FirstOrgId;
			public readonly string FirstCountryId;
			public readonly string FirstProvinceId;
			public readonly int[] SpeedMultipliers;

			public Fixture(GameLogic logic, int gameTimeEntity, ResourceCollectorRegistry collectorRegistry,
				IReadOnlyList<string> resourceIdUpdateOrder, string firstOrgId, string firstCountryId,
				string firstProvinceId, int[] speedMultipliers) {
				Logic = logic;
				GameTimeEntity = gameTimeEntity;
				CollectorRegistry = collectorRegistry;
				ResourceIdUpdateOrder = resourceIdUpdateOrder;
				FirstOrgId = firstOrgId;
				FirstCountryId = firstCountryId;
				FirstProvinceId = firstProvinceId;
				SpeedMultipliers = speedMultipliers;
			}
		}

		public static Fixture Build() {
			var orgConfig = new FileConfig<OrganizationConfig>(Path.Combine(ConfigDir, "organizations.json")).Load();
			var orgIds = new List<string>();
			foreach (var entry in orgConfig.Organizations) { orgIds.Add(entry.OrganizationId); }

			var ctx = GS.Game.ConsoleRunner.Program.BuildContext(ConfigDir, rngSeed: Seed, participatingOrganizationIds: orgIds,
				initialOrganizationId: orgIds.Count > 0 ? orgIds[0] : "", logger: null);
			var logic = new GameLogic(ctx);
			// Triggers InitSystem once - populates all countries/provinces/orgs from the committed configs.
			logic.Update(24f);

			int gameTimeEntity = -1;
			int[] required = { TypeId<GameTime>.Value };
			foreach (var arch in logic.World.GetMatchingArchetypes(required, null)) {
				gameTimeEntity = arch.Entities[0];
				break;
			}
			if (gameTimeEntity < 0) {
				throw new InvalidOperationException("GameWorldFixture: no GameTime entity found after init tick.");
			}

			string firstCountryId = logic.ProvinceConfig.Provinces.Count > 0 ? logic.ProvinceConfig.Provinces[0].CountryId : "";
			string firstProvinceId = logic.ProvinceConfig.Provinces.Count > 0 ? logic.ProvinceConfig.Provinces[0].ProvinceId : "";

			// Independently loads the same committed game_settings.json GameLogic's own constructor
			// already read, so this standalone registry matches production values exactly without
			// needing any new GameLogic API.
			var settings = new FileConfig<GameSettings>(Path.Combine(ConfigDir, "game_settings.json")).Load();
			var registry = ResourceCollectorRegistry.CreateDefault(
				settings.PopulationGrowthPercentPerMonth, settings.CountryScoreCoefficient,
				settings.RecruitsInitialPercent, settings.RecruitsCapPercent, settings.RecruitsMonthlyIncreasePercent);

			return new Fixture(logic, gameTimeEntity, registry, settings.ResourceIdUpdateOrder,
				orgIds.Count > 0 ? orgIds[0] : "", firstCountryId, firstProvinceId, settings.SpeedMultipliers);
		}
	}
}
