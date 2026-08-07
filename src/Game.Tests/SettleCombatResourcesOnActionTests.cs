using System;
using System.Collections.Generic;
using ECS;
using GS.Configs;
using GS.Game.Commands;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SettleCombatResourcesOnActionTests {
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		const string OrgId = "Illuminati";
		const string CountryId = "Great_Britain";
		const string OpponentId = "France";
		const string ActionId = "boost_damage";
		const string EffectId = "boost_damage_effect";
		const int BaseDamage = 40;
		const int OpponentBaseDamage = 40;
		const int BaseDurability = 40;

		static GameLogic BuildLogic() {
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry {
						CountryId = CountryId,
						DisplayName = "Great Britain",
						IsAvailable = true,
						BaseDamage = BaseDamage,
						BaseDurability = BaseDurability
					},
					new CountryEntry {
						CountryId = OpponentId,
						DisplayName = "France",
						IsAvailable = true,
						BaseDamage = OpponentBaseDamage,
						BaseDurability = BaseDurability
					}
				}
			};
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = OrgId,
						DisplayName = "Illuminati",
						HqCountryId = CountryId,
						InitialGold = 1000.0
					}
				}
			};
			var actionConfig = new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = ActionId,
						OwnerType = "country",
						EffectIds = new List<string> { EffectId }
					}
				}
			};
			var effectConfig = new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new CountryResourceModifierEffectParams {
						EffectId = EffectId,
						EffectType = "CountryResourceModifier",
						ResourceId = ResourceDefinitions.TroopsDamageBonusPercent,
						InitialValue = 10.0,
						DecayPerMonth = 1.0
					}
				}
			};
			var resourceConfig = new ResourceConfig {
				Resources = new List<ResourceDefinition> {
					new ResourceDefinition { ResourceId = ResourceDefinitions.Damage, SeedTarget = ResourceSeedTarget.Country },
					new ResourceDefinition { ResourceId = ResourceDefinitions.Durability, SeedTarget = ResourceSeedTarget.Country },
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.TroopsDamageBonusPercent,
						SeedTarget = ResourceSeedTarget.Country,
						DefaultInitialValue = 0.0
					},
					new ResourceDefinition {
						ResourceId = ResourceDefinitions.Recruits,
						SeedTarget = ResourceSeedTarget.Country,
						DefaultInitialValue = 20.0
					}
				}
			};

			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(countryConfig),
				new StaticConfig<GameSettings>(new GameSettings()),
				new StaticConfig<ResourceConfig>(resourceConfig),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: OrgId,
				action: new StaticConfig<ActionConfig>(actionConfig),
				effect: new StaticConfig<EffectConfig>(effectConfig));
			return new GameLogic(ctx);
		}

		static int AddCardInHand(World world) {
			int e = world.Create();
			world.Add(e, new GameAction { ActionId = ActionId });
			world.Add(e, new OrgContext { OrgId = OrgId });
			world.Add(e, new CardOwnerType(CardOwnerKind.Country));
			world.Add(e, new CountryContext { CountryId = CountryId });
			world.Add(e, new CardInHand { SlotIndex = 0 });
			return e;
		}

		[Fact]
		void playing_a_damage_bonus_card_settles_damage_the_same_tick() {
			var logic = BuildLogic();
			logic.Update(0f);
			AddCardInHand(logic.World);

			double damageBefore = logic.Resources.GetValue(logic.World, CountryId, ResourceDefinitions.Damage);
			Assert.Equal(BaseDamage, damageBefore);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = ActionId });
			logic.Update(0f);

			Assert.Equal(10.0, logic.Resources.GetValue(logic.World, CountryId, ResourceDefinitions.TroopsDamageBonusPercent));
			Assert.Equal(BaseDamage * 1.1, logic.Resources.GetValue(logic.World, CountryId, ResourceDefinitions.Damage), 6);
		}

		[Fact]
		void peacetime_damage_bonus_raises_win_percent_from_live_damage_same_tick() {
			var logic = BuildLogic();
			logic.Update(0f);
			EnsureRecruits(logic, CountryId, 20);
			EnsureRecruits(logic, OpponentId, 20);
			AddCardInHand(logic.World);

			int winPercentBefore = WarWinChanceEstimator.EstimateAttackerWinPercent(
				logic.World, logic.Resources, CountryId, OpponentId, 0, 0);
			Assert.Equal(50, winPercentBefore);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = ActionId });
			logic.Update(0f);

			Assert.Equal(BaseDamage * 1.1, logic.Resources.GetValue(logic.World, CountryId, ResourceDefinitions.Damage), 6);
			int winPercentAfter = WarWinChanceEstimator.EstimateAttackerWinPercent(
				logic.World, logic.Resources, CountryId, OpponentId, 0, 0);
			Assert.True(winPercentAfter > winPercentBefore);
		}

		static void EnsureRecruits(GameLogic logic, string countryId, double value) {
			if (!ResourceMutations.TrySetValue(logic.Resources, logic.World, countryId, ResourceDefinitions.Recruits, value, out _)) {
				logic.Resources.Set(logic.World, countryId, ResourceDefinitions.Recruits, value, OwnerType.Country);
			}
		}

		[Fact]
		void peacetime_damage_bonus_survives_declare_war() {
			var logic = BuildLogic();
			logic.Update(0f);
			AddCardInHand(logic.World);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = CountryId, ActionId = ActionId });
			logic.Update(0f);

			Assert.Equal(10.0, logic.Resources.GetValue(logic.World, CountryId, ResourceDefinitions.TroopsDamageBonusPercent));
			Assert.False(Wars.IsInWar(logic.World, CountryId));

			Assert.True(Wars.DeclareWar(logic.World, logic.Resources, CountryId, OpponentId, new DateTime(1880, 1, 1)));
			Assert.True(Wars.IsInWar(logic.World, CountryId));
			Assert.Equal(10.0, logic.Resources.GetValue(logic.World, CountryId, ResourceDefinitions.TroopsDamageBonusPercent));
		}
	}
}
