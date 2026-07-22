using System;
using System.IO;
using System.Linq;
using GS.Configs.IO;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class ResourceConfigTests {
		[Fact]
		void file_config_deserializes_named_resource_seed_targets() {
			string filePath = Path.GetTempFileName();
			try {
				File.WriteAllText(filePath, """
					{
						"displayWhitelist": ["org_score"],
						"resources": [
							{ "resourceId": "org_score", "seedTarget": "Org" },
							{ "resourceId": "population", "seedTarget": "Province" }
						]
					}
					""");

				ResourceConfig config = new FileConfig<ResourceConfig>(filePath).Load();

				Assert.Equal(new[] { "org_score" }, config.DisplayWhitelist);
				Assert.Equal(ResourceSeedTarget.Org, config.FindResource("org_score")?.SeedTarget);
				Assert.Equal(new[] { "org_score" },
					config.FindResources(ResourceSeedTarget.Org).Select(resource => resource.ResourceId));
				Assert.Equal(new[] { "population" },
					config.FindResources(ResourceSeedTarget.Province).Select(resource => resource.ResourceId));
			} finally {
				File.Delete(filePath);
			}
		}

		[Fact]
		void resource_definition_defaults_seed_target_to_country() {
			var inMemoryDefinition = new ResourceDefinition();

			Assert.Equal(ResourceSeedTarget.Country, inMemoryDefinition.SeedTarget);

			string filePath = Path.GetTempFileName();
			try {
				File.WriteAllText(filePath, """
					{ "resources": [{ "resourceId": "legacy" }] }
					""");

				ResourceConfig config = new FileConfig<ResourceConfig>(filePath).Load();

				Assert.Equal(ResourceSeedTarget.Country, config.FindResource("legacy")?.SeedTarget);
			} finally {
				File.Delete(filePath);
			}
		}
	}
}
