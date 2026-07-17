using System.Collections.Generic;
using GS.Configs;
using GS.Game.Configs;
using GS.Main;

namespace GS.Game.Tests {
	static class MultiOrgTestSupport {
		public const string OrgA = "Illuminati";
		public const string OrgB = "Masons";
		public const string OrgC = "Templars";
		public const string HqA = "Great_Britain";
		public const string HqB = "France";
		public const string HqC = "Prussia";
		public const string ExtraCountry1 = "Prussia";
		public const string ExtraCountry2 = "Austria";
		public const string DiscoverActionId = "spread_rumors";
		public const string SpendGoldActionId = "spend_gold";
		public const string CountryCardActionId = "influence_country";
		public const double CountryCardGoldCost = 20.0;

		public sealed class StaticConfig<T> : IConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		public static GameLogicContext BuildContext(
			IReadOnlyList<string>? participatingOrganizationIds = null,
			int? rngSeed = null,
			string initialOrganizationId = OrgA,
			string initialPlayerCountryId = HqA,
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null,
			IGameLogger? logger = null,
			CharacterConfig? characterConfig = null,
			bool includeCountryCard = false) {

			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = HqA, DisplayName = "Great Britain", IsAvailable = true },
					new CountryEntry { CountryId = HqB, DisplayName = "France", IsAvailable = true },
					new CountryEntry { CountryId = ExtraCountry1, DisplayName = "Prussia", IsAvailable = true },
					new CountryEntry { CountryId = ExtraCountry2, DisplayName = "Austria", IsAvailable = true }
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = OrgA, DisplayName = "Illuminati", HqCountryId = HqA,
						InitialGold = 1000.0, BaseControl = 10, InitialAgentSlots = 1
					},
					new OrganizationEntry {
						OrganizationId = OrgB, DisplayName = "Masons", HqCountryId = HqB,
						InitialGold = 500.0, BaseControl = 10, InitialAgentSlots = 1
					},
					new OrganizationEntry {
						OrganizationId = OrgC, DisplayName = "Templars", HqCountryId = HqC,
						InitialGold = 300.0, BaseControl = 10, InitialAgentSlots = 1
					}
				}
			};
			var gameSettings = new GameSettings {
				StartYear = 1880,
				DefaultLocale = "en",
				SpeedMultipliers = new[] { 1, 24, 720 },
				AutoSaveInterval = "monthly"
			};
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition {
						ResourceId = "gold",
						DefaultInitialValue = 0.0,
						DefaultEffects = new List<EffectDefinition> {
							new EffectDefinition { EffectId = "base_income", Value = 100.0, PayType = "Monthly" }
						}
					}
				}
			};
			var actionConfig = new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "org", HandSize = 2 },
					new ActionOwnerDefaults { OwnerType = "country", HandSize = includeCountryCard ? 2 : 0 }
				},
				OrgPools = new List<OrgActionPool> {
					new OrgActionPool { OrgId = OrgA, ActionIds = new List<string> { DiscoverActionId, SpendGoldActionId } },
					new OrgActionPool { OrgId = OrgB, ActionIds = new List<string> { DiscoverActionId, SpendGoldActionId } },
					new OrgActionPool { OrgId = OrgC, ActionIds = new List<string> { DiscoverActionId, SpendGoldActionId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = DiscoverActionId,
						OwnerType = "org",
						EffectIds = new List<string> { "discover" }
					},
					new ActionDefinition {
						ActionId = SpendGoldActionId,
						OwnerType = "org",
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } }
					}
				}
			};
			if (includeCountryCard) {
				// Condition is trivially true (control >= 0) so the card is eligible in any discovered
				// country from init; playability toggling for tests is driven via cost/gold instead.
				actionConfig.Actions.Add(new ActionDefinition {
					ActionId = CountryCardActionId,
					OwnerType = "country",
					Conditions = new List<ExpressionNode> {
						new ExpressionNode {
							Type = "gte",
							Members = new List<ExpressionNode> {
								new ExpressionNode { Type = "control" },
								new ExpressionNode { Type = "value", Value = 0 }
							}
						}
					},
					Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = CountryCardGoldCost } }
				});
			}
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new DiscoverCountryEffectParams { EffectId = "discover", EffectType = "DiscoverCountry" }
				}
			};
			var geoJson = new GeoJsonConfig();
			var mapEntry = new MapEntryConfig();

			return new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(geoJson),
				new StaticConfig<MapEntryConfig>(mapEntry),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(gameSettings),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				storage: storage,
				serializer: serializer,
				logger: logger,
				initialPlayerCountryId: initialPlayerCountryId,
				initialOrganizationId: initialOrganizationId,
				character: characterConfig != null ? new StaticConfig<CharacterConfig>(characterConfig) : null,
				action: new StaticConfig<ActionConfig>(actionConfig),
				effect: new StaticConfig<EffectConfig>(effectConfig),
				rngSeed: rngSeed,
				participatingOrganizationIds: participatingOrganizationIds);
		}
	}
}
