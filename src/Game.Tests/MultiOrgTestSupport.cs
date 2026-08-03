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
		public const string SampleOrgActionId = "sample_org_action";
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
			IPersistentStorage? storage = null,
			ISnapshotSerializer? serializer = null,
			IGameLogger? logger = null,
			CharacterConfig? characterConfig = null,
			bool includeCountryCard = false,
			CompletionConditionConfig? completionCondition = null,
			int? maxControlPool = null) {

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
			if (completionCondition != null) { gameSettings.CompletionCondition = completionCondition; }
			if (maxControlPool.HasValue) { gameSettings.MaxControlPool = maxControlPool.Value; }
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
					new OrgActionPool { OrgId = OrgA, ActionIds = new List<string> { SampleOrgActionId, SpendGoldActionId } },
					new OrgActionPool { OrgId = OrgB, ActionIds = new List<string> { SampleOrgActionId, SpendGoldActionId } },
					new OrgActionPool { OrgId = OrgC, ActionIds = new List<string> { SampleOrgActionId, SpendGoldActionId } }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = SampleOrgActionId,
						OwnerType = "org"
					},
					new ActionDefinition {
						ActionId = SpendGoldActionId,
						OwnerType = "org",
						Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } }
					}
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition>()
			};
			if (includeCountryCard) {
				// Condition is trivially true (control >= 0) so the card is eligible in any
				// country from init; playability toggling for tests is driven via cost/gold instead.
				// Positive ControlChange so ControlFeature / RaisesControl classification work.
				actionConfig.Actions.Add(new ActionDefinition {
					ActionId = CountryCardActionId,
					OwnerType = "country",
					EffectIds = new List<string> { "control_pos" },
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
				effectConfig.Effects.Add(new ControlChangeEffectParams {
					EffectId = "control_pos", EffectType = "ControlChange", Amount = 5
				});
				// Relation-synced cards: DeckCopies is draw weight (InitSystem skips these ids).
				actionConfig.Actions.Add(new ActionDefinition {
					ActionId = "stop_friendship",
					OwnerType = "country",
					DeckCopies = 1
				});
				actionConfig.Actions.Add(new ActionDefinition {
					ActionId = "stop_rivalry",
					OwnerType = "country",
					DeckCopies = 1
				});
				actionConfig.Actions.Add(new ActionDefinition {
					ActionId = "declare_war",
					OwnerType = "country",
					DeckCopies = 1
				});
			}
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
				initialOrganizationId: initialOrganizationId,
				character: characterConfig != null ? new StaticConfig<CharacterConfig>(characterConfig) : null,
				action: new StaticConfig<ActionConfig>(actionConfig),
				effect: new StaticConfig<EffectConfig>(effectConfig),
				rngSeed: rngSeed,
				participatingOrganizationIds: participatingOrganizationIds);
		}
	}
}
