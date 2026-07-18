using System.Collections.Generic;
using GS.Configs;
using GS.Game.Configs;
using GS.Main;

namespace GlobalStrategy.Benchmarks;

static class BenchmarkGameFactory {
	public const string PlayerOrgId = "Illuminati";
	public const string RivalOrgId = "Templars";
	public const string HqCountryId = "Great_Britain";
	public const string OtherCountryId = "France";

	public static GameLogic CreateGameLogic() {
		var ctx = new GameLogicContext(
			new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
			new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
			new StaticConfig<CountryConfig>(CreateCountryConfig()),
			new StaticConfig<GameSettings>(CreateGameSettings()),
			new StaticConfig<ResourceConfig>(new ResourceConfig { Resources = new List<ResourceDefinition>() }),
			new StaticConfig<OrganizationConfig>(CreateOrganizationConfig()),
			initialOrganizationId: PlayerOrgId,
			action: new StaticConfig<ActionConfig>(CreateActionConfig()),
			effect: new StaticConfig<EffectConfig>(new EffectConfig()),
			character: new StaticConfig<CharacterConfig>(new CharacterConfig()),
			rngSeed: 12345);
		return new GameLogic(ctx);
	}

	static CountryConfig CreateCountryConfig() {
		return new CountryConfig {
			Countries = new List<CountryEntry> {
				new CountryEntry { CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true },
				new CountryEntry { CountryId = OtherCountryId, DisplayName = "France", IsAvailable = true }
			}
		};
	}

	static OrganizationConfig CreateOrganizationConfig() {
		return new OrganizationConfig {
			Organizations = new List<OrganizationEntry> {
				new OrganizationEntry {
					OrganizationId = PlayerOrgId,
					DisplayName = "Illuminati",
					HqCountryId = HqCountryId,
					InitialGold = 1000.0
				},
				new OrganizationEntry {
					OrganizationId = RivalOrgId,
					DisplayName = "Templars",
					HqCountryId = OtherCountryId,
					InitialGold = 1000.0
				}
			}
		};
	}

	static GameSettings CreateGameSettings() {
		return new GameSettings {
			StartYear = 1880,
			DefaultLocale = "en",
			SpeedMultipliers = new[] { 1, 24, 720 },
			AutoSaveInterval = "monthly",
			PopulationGrowthPercentPerMonth = 0.075,
			CountryScoreCoefficient = 1.0,
			BotActionLogRetentionCap = 500,
			MaxControlPool = 100,
			BotFeatures = new List<BotFeatureConfigEntry>()
		};
	}

	static ActionConfig CreateActionConfig() {
		return new ActionConfig {
			Defaults = new List<ActionOwnerDefaults> {
				new ActionOwnerDefaults { OwnerType = "org", HandSize = 3 },
				new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
			},
			OrgPools = new List<OrgActionPool> {
				new OrgActionPool {
					OrgId = PlayerOrgId,
					ActionIds = new List<string> { "spread_rumors", "build_network", "buy_influence" }
				},
				new OrgActionPool {
					OrgId = RivalOrgId,
					ActionIds = new List<string> { "spread_rumors", "build_network", "buy_influence" }
				}
			},
			Actions = new List<ActionDefinition> {
				new ActionDefinition { ActionId = "spread_rumors", OwnerType = "org", Cost = new List<ActionCost>() },
				new ActionDefinition { ActionId = "build_network", OwnerType = "org", Cost = new List<ActionCost>() },
				new ActionDefinition { ActionId = "buy_influence", OwnerType = "org", Cost = new List<ActionCost>() }
			}
		};
	}

	sealed class StaticConfig<T> : IConfigSource<T> {
		readonly T _value;

		public StaticConfig(T value) {
			_value = value;
		}

		public T Load() {
			return _value;
		}
	}
}
