using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class SellArmsEffectTests {
		const string ActionId = "sell_arms";
		const string CountryEffectId = "sell_arms_damage_bonus_effect";
		const string OrgEffectId = "sell_arms_gold_grant_effect";
		const string OrgId = "org_a";
		const string CountryId = "country_a";
		static readonly DateTime CurrentTime = new DateTime(1880, 1, 15, 12, 0, 0);

		static ActionConfig BuildActionConfig() {
			return new ActionConfig {
				Actions = new List<ActionDefinition> {
					new ActionDefinition {
						ActionId = ActionId,
						OwnerType = "country",
						EffectIds = new List<string> { CountryEffectId, OrgEffectId }
					}
				}
			};
		}

		static EffectConfig BuildEffectConfig() {
			return new EffectConfig {
				Effects = new List<ActionEffectDefinition> {
					new CountryResourceModifierEffectParams {
						EffectId = CountryEffectId,
						EffectType = "CountryResourceModifier",
						ResourceId = ResourceDefinitions.TroopsDamageBonusPercent,
						InitialValue = 10.0,
						DecayPerMonth = 1.0
					},
					new OrgResourceGrantEffectParams {
						EffectId = OrgEffectId,
						EffectType = "OrgResourceGrant",
						ResourceId = ResourceDefinitions.Gold,
						Amount = 300.0
					}
				}
			};
		}

		static int AddResource(World world, string ownerId, OwnerType ownerType, string resourceId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(ownerId, ownerType));
			world.Add(entity, new Resource { ResourceId = resourceId, Value = value });
			return entity;
		}

		static int AddSucceededAction(World world) {
			int entity = world.Create();
			world.Add(entity, new GameAction { ActionId = ActionId });
			world.Add(entity, new ActionSucceeded());
			world.Add(entity, new OrgContext { OrgId = OrgId });
			world.Add(entity, new CountryContext { CountryId = CountryId });
			world.Add(entity, new CardUse());
			return entity;
		}

		[Fact]
		void sell_arms_mutates_only_selected_owners_and_creates_bounded_monthly_decay() {
			var world = new World();
			int countryBonus = AddResource(
				world, CountryId, OwnerType.Country, ResourceDefinitions.TroopsDamageBonusPercent, 0);
			int otherCountryBonus = AddResource(
				world, "country_b", OwnerType.Country, ResourceDefinitions.TroopsDamageBonusPercent, 0);
			int orgGold = AddResource(world, OrgId, OwnerType.Org, ResourceDefinitions.Gold, 100);
			int otherOrgGold = AddResource(world, "org_b", OwnerType.Org, ResourceDefinitions.Gold, 100);
			int cardEntity = AddSucceededAction(world);

			CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime);

			Assert.Equal(10.0, world.Get<Resource>(countryBonus).Value);
			Assert.Equal(0.0, world.Get<Resource>(otherCountryBonus).Value);
			Assert.Equal(400.0, world.Get<Resource>(orgGold).Value);
			Assert.Equal(100.0, world.Get<Resource>(otherOrgGold).Value);

			var changes = new List<ResourceChange>();
			int[] changeRequired = { TypeId<ResourceChange>.Value };
			foreach (Archetype archetype in world.GetMatchingArchetypes(changeRequired, null)) {
				ResourceChange[] column = archetype.GetColumn<ResourceChange>();
				for (int i = 0; i < archetype.Count; i++) {
					changes.Add(column[i]);
				}
			}
			Assert.Contains(changes, change =>
				change.ResourceId == ResourceDefinitions.TroopsDamageBonusPercent &&
				change.OwnerId == CountryId && change.Amount == 10.0);
			Assert.Contains(changes, change =>
				change.ResourceId == ResourceDefinitions.Gold &&
				change.OwnerId == OrgId && change.Amount == 300.0);
			Assert.Equal(2, changes.Count);

			int[] effectRequired = {
				TypeId<ResourceOwner>.Value,
				TypeId<ResourceLink>.Value,
				TypeId<ResourceEffect>.Value
			};
			ResourceEffect? decay = null;
			ResourceOwner? decayOwner = null;
			ResourceLink? decayLink = null;
			foreach (Archetype archetype in world.GetMatchingArchetypes(effectRequired, null)) {
				ResourceOwner[] owners = archetype.GetColumn<ResourceOwner>();
				ResourceLink[] links = archetype.GetColumn<ResourceLink>();
				ResourceEffect[] effects = archetype.GetColumn<ResourceEffect>();
				for (int i = 0; i < archetype.Count; i++) {
					decayOwner = owners[i];
					decayLink = links[i];
					decay = effects[i];
				}
			}

			ResourceOwner owner = Assert.IsType<ResourceOwner>(decayOwner);
			ResourceLink link = Assert.IsType<ResourceLink>(decayLink);
			ResourceEffect effect = Assert.IsType<ResourceEffect>(decay);
			Assert.Equal(CountryId, owner.OwnerId);
			Assert.Equal(OwnerType.Country, owner.OwnerType);
			Assert.Equal(ResourceDefinitions.TroopsDamageBonusPercent, link.ResourceId);
			Assert.Equal(-1.0, effect.Value);
			Assert.Equal(PayType.Monthly, effect.PayType);
			Assert.Equal(10.0, effect.MaxTotal);
			Assert.True(effect.ClampToZero);
			Assert.Contains(OrgId, effect.EffectId);
			Assert.Contains(CountryId, effect.EffectId);
			Assert.Contains(cardEntity.ToString(), effect.EffectId);
			Assert.Contains(CurrentTime.Ticks.ToString(), effect.EffectId);

			ResourceSystem.Update(world, CurrentTime, CurrentTime.AddDays(1));
			Assert.Equal(10.0, world.Get<Resource>(countryBonus).Value);
			ResourceSystem.Update(world, new DateTime(1880, 1, 31), new DateTime(1880, 2, 1));
			Assert.Equal(9.0, world.Get<Resource>(countryBonus).Value);
			for (int month = 2; month <= 10; month++) {
				DateTime previous = new DateTime(1880, month, 15);
				ResourceSystem.Update(world, previous, previous.AddMonths(1));
			}
			Assert.Equal(0.0, world.Get<Resource>(countryBonus).Value);
			ResourceSystem.Update(world, new DateTime(1880, 11, 15), new DateTime(1880, 12, 15));
			Assert.Equal(0.0, world.Get<Resource>(countryBonus).Value);
		}

		[Fact]
		void independently_bounded_decay_sources_stop_without_interrupting_each_other() {
			var world = new World();
			int countryBonus = AddResource(
				world, CountryId, OwnerType.Country, ResourceDefinitions.TroopsDamageBonusPercent, 20);

			int oldEffectEntity = world.Create();
			world.Add(oldEffectEntity, new ResourceOwner(CountryId, OwnerType.Country));
			world.Add(oldEffectEntity, new ResourceLink(ResourceDefinitions.TroopsDamageBonusPercent));
			world.Add(oldEffectEntity, new ResourceEffect {
				EffectId = "old_sell_arms",
				Value = -1,
				PayType = PayType.Monthly,
				AccumulatedTotal = 9,
				MaxTotal = 10,
				ClampToZero = true
			});
			int newEffectEntity = world.Create();
			world.Add(newEffectEntity, new ResourceOwner(CountryId, OwnerType.Country));
			world.Add(newEffectEntity, new ResourceLink(ResourceDefinitions.TroopsDamageBonusPercent));
			world.Add(newEffectEntity, new ResourceEffect {
				EffectId = "new_sell_arms",
				Value = -1,
				PayType = PayType.Monthly,
				MaxTotal = 10,
				ClampToZero = true
			});

			ResourceSystem.Update(world, new DateTime(1880, 1, 15), new DateTime(1880, 2, 15));
			Assert.Equal(18.0, world.Get<Resource>(countryBonus).Value);
			Assert.Equal(10.0, world.Get<ResourceEffect>(oldEffectEntity).AccumulatedTotal);
			Assert.Equal(1.0, world.Get<ResourceEffect>(newEffectEntity).AccumulatedTotal);

			ResourceSystem.Update(world, new DateTime(1880, 2, 15), new DateTime(1880, 3, 15));
			Assert.Equal(17.0, world.Get<Resource>(countryBonus).Value);
			Assert.Equal(10.0, world.Get<ResourceEffect>(oldEffectEntity).AccumulatedTotal);
			Assert.Equal(2.0, world.Get<ResourceEffect>(newEffectEntity).AccumulatedTotal);
		}

		[Fact]
		void sell_arms_fails_with_context_when_seeded_country_resource_is_missing() {
			var world = new World();
			AddResource(world, OrgId, OwnerType.Org, ResourceDefinitions.Gold, 100);
			AddSucceededAction(world);

			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
				CreateActionEffectSystem.Update(world, BuildActionConfig(), BuildEffectConfig(), CurrentTime));

			Assert.Contains(ActionId, exception.Message);
			Assert.Contains(CountryId, exception.Message);
			Assert.Contains(ResourceDefinitions.TroopsDamageBonusPercent, exception.Message);
		}
	}
}
