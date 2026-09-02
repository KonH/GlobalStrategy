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
	// Mirrors WarsTests.debug_commands_declare_and_stop_war_through_game_logic /
	// DamageDurabilityGameLogicTests fixture style. Covers the full revenge play path through
	// GameLogic.Update: war declaration, RevengeWarBonus attachment, and same-tick
	// SettleCombatResources() visibility of the damage/durability multiplier.
	public class RevengeCardGameLogicTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		sealed class StaticConfig<T> : IReadOnlyConfigSource<T> {
			readonly T _value;
			public StaticConfig(T value) => _value = value;
			public T Load() => _value;
		}

		const string OrgId = "Illuminati";
		const string HqCountryId = "Great_Britain";
		const string TargetCountryId = "France";
		const int HqBaseDamage = 70;
		const int HqBaseDurability = 65;
		const int TargetBaseDamage = 80;
		const int TargetBaseDurability = 75;
		const string MilitaryAdvisorCharId = "gb_mil";

		static CharacterConfig BuildCharacterConfig() => new CharacterConfig {
			Roles = new List<CharacterRoleDefinition> {
				new CharacterRoleDefinition { RoleId = "military_advisor" }
			},
			CountryPools = new List<CountryCharacterPool> {
				new CountryCharacterPool {
					CountryId = HqCountryId,
					Slots = new Dictionary<string, List<CharacterEntry>> {
						["military_advisor"] = new List<CharacterEntry> { new CharacterEntry { CharacterId = MilitaryAdvisorCharId } }
					}
				}
			}
		};

		static ActionConfig BuildActionConfig() => new ActionConfig {
			Defaults = new List<ActionOwnerDefaults> {
				new ActionOwnerDefaults { OwnerType = "country", HandSize = 1 }
			},
			Actions = new List<ActionDefinition> {
				new ActionDefinition {
					ActionId = "declare_revenge_war",
					OwnerType = "country",
					TargetRole = "military_advisor",
					Chance = 1,
					Conditions = new List<ExpressionNode> {
						new ExpressionNode {
							Type = "gte",
							Members = new List<ExpressionNode> {
								new ExpressionNode { Type = "control" },
								new ExpressionNode { Type = "value", Value = 20 }
							}
						},
						new ExpressionNode {
							Type = "gte",
							Members = new List<ExpressionNode> {
								new ExpressionNode { Type = "opinion" },
								new ExpressionNode { Type = "value", Value = 25 }
							}
						},
						new ExpressionNode {
							Type = "gte",
							Members = new List<ExpressionNode> {
								new ExpressionNode { Type = "warFree" },
								new ExpressionNode { Type = "value", Value = 1 }
							}
						}
					},
					Cost = new List<ActionCost> { new ActionCost { ResourceId = "gold", Amount = 50.0 } },
					EffectIds = new List<string> { "declare_revenge_war_effect" }
				}
			}
		};

		static EffectConfig BuildEffectConfig() => new EffectConfig {
			Effects = new List<ActionEffectDefinition> {
				new DeclareRevengeWarEffectParams {
					EffectId = "declare_revenge_war_effect",
					EffectType = "DeclareRevengeWar",
					DamageBonusPercent = 10.0,
					DurabilityBonusPercent = 5.0
				}
			}
		};

		static CountryConfig BuildCountryConfig() => new CountryConfig {
			Countries = new List<CountryEntry> {
				new CountryEntry {
					CountryId = HqCountryId, DisplayName = "Great Britain", IsAvailable = true,
					BaseDamage = HqBaseDamage, BaseDurability = HqBaseDurability
				},
				new CountryEntry {
					CountryId = TargetCountryId, DisplayName = "France", IsAvailable = true,
					BaseDamage = TargetBaseDamage, BaseDurability = TargetBaseDurability
				}
			}
		};

		static ResourceConfig BuildResourceConfig() => new ResourceConfig {
			Resources = new List<ResourceDefinition> {
				new ResourceDefinition { ResourceId = ResourceDefinitions.Damage, SeedTarget = ResourceSeedTarget.Country, DefaultInitialValue = 0.0 },
				new ResourceDefinition { ResourceId = ResourceDefinitions.Durability, SeedTarget = ResourceSeedTarget.Country, DefaultInitialValue = 0.0 }
			}
		};

		static GameLogic BuildLogic() {
			var orgConfig = new OrganizationConfig {
				Organizations = new List<OrganizationEntry> {
					new OrganizationEntry {
						OrganizationId = OrgId, DisplayName = "Illuminati", HqCountryId = HqCountryId, InitialGold = 1000.0
					}
				}
			};
			var ctx = new GameLogicContext(
				new StaticConfig<GeoJsonConfig>(new GeoJsonConfig()),
				new StaticConfig<MapEntryConfig>(new MapEntryConfig()),
				new StaticConfig<CountryConfig>(BuildCountryConfig()),
				new StaticConfig<GameSettings>(new GameSettings()),
				new StaticConfig<ResourceConfig>(BuildResourceConfig()),
				new StaticConfig<OrganizationConfig>(orgConfig),
				initialOrganizationId: OrgId,
				character: new StaticConfig<CharacterConfig>(BuildCharacterConfig()),
				action: new StaticConfig<ActionConfig>(BuildActionConfig()),
				effect: new StaticConfig<EffectConfig>(BuildEffectConfig()),
				province: new StaticConfig<ProvinceConfig>(new ProvinceConfig()));
			return new GameLogic(ctx);
		}

		static void AddControl(World world, string orgId, string countryId, int value) {
			int e = world.Create();
			world.Add(e, new ControlEffect { OrgId = orgId, CountryId = countryId, Value = value, EffectId = $"test_{orgId}_{countryId}" });
		}

		static void AddOpinion(World world, string charId, string orgId, int opinion) {
			int e = world.Create();
			world.Add(e, new ResourceOwner(charId, OwnerType.Character));
			world.Add(e, new Resource { ResourceId = $"opinion_{orgId}", Value = opinion });
		}

		static void PutRevengeCardInHand(World world, string orgId, string countryId, string targetCountryId) {
			int[] required = { TypeId<GameAction>.Value, TypeId<OrgContext>.Value, TypeId<CountryContext>.Value };
			foreach (var arch in world.GetMatchingArchetypes(required, null)) {
				GameAction[] actions = arch.GetColumn<GameAction>();
				OrgContext[] orgs = arch.GetColumn<OrgContext>();
				CountryContext[] countries = arch.GetColumn<CountryContext>();
				for (int i = 0; i < arch.Count; i++) {
					if (actions[i].ActionId == "declare_revenge_war" && orgs[i].OrgId == orgId && countries[i].CountryId == countryId
						&& world.Has<RevengeCardTarget>(arch.Entities[i])
						&& world.Get<RevengeCardTarget>(arch.Entities[i]).TargetCountryId == targetCountryId) {
						int entity = arch.Entities[i];
						if (!world.Has<CardInHand>(entity)) {
							world.Add(entity, new CardInHand { SlotIndex = 0 });
						}
						return;
					}
				}
			}
			throw new InvalidOperationException($"Revenge card not found: org={orgId} country={countryId}");
		}

		static RevengeWarBonus? FindBonus(World world, string countryId) {
			int[] req = { TypeId<RevengeWarBonus>.Value };
			foreach (var arch in world.GetMatchingArchetypes(req, null)) {
				RevengeWarBonus[] bonuses = arch.GetColumn<RevengeWarBonus>();
				for (int i = 0; i < arch.Count; i++) {
					if (bonuses[i].CountryId == countryId) { return bonuses[i]; }
				}
			}
			return null;
		}

		[Fact]
		void playing_revenge_declares_war_attaches_bonus_and_applies_multiplier_same_tick() {
			var logic = BuildLogic();
			logic.Update(0f);

			RevengeEligibilityQuery.SetEligible(logic.World, HqCountryId, TargetCountryId);
			logic.Update(0f);
			AddControl(logic.World, OrgId, HqCountryId, 20);
			AddOpinion(logic.World, MilitaryAdvisorCharId, OrgId, 25);
			PutRevengeCardInHand(logic.World, OrgId, HqCountryId, TargetCountryId);

			logic.Commands.Push(new PlayCardActionCommand { OrgId = OrgId, CountryId = HqCountryId, ActionId = "declare_revenge_war", TargetCountryId = TargetCountryId });
			logic.Update(0f);

			Assert.True(Wars.IsInWar(logic.World, HqCountryId));
			Assert.True(Wars.IsInWar(logic.World, TargetCountryId));

			var bonus = FindBonus(logic.World, HqCountryId);
			Assert.NotNull(bonus);
			Assert.Equal(10.0, bonus!.Value.DamageBonusPercent);
			Assert.Equal(5.0, bonus.Value.DurabilityBonusPercent);
			Assert.Null(FindBonus(logic.World, TargetCountryId));

			// Same tick, no waiting for the next day boundary: SettleCombatResources() must have
			// already re-run the Daily damage/durability collectors after CreateActionEffectSystem
			// attached RevengeWarBonus.
			Assert.Equal(HqBaseDamage * 1.10, _resources.GetValue(logic.World, HqCountryId, ResourceDefinitions.Damage), 6);
			Assert.Equal(HqBaseDurability * 1.05, _resources.GetValue(logic.World, HqCountryId, ResourceDefinitions.Durability), 6);

			// Defender is unaffected — the bonus is attacker-only.
			Assert.Equal(TargetBaseDamage, _resources.GetValue(logic.World, TargetCountryId, ResourceDefinitions.Damage), 6);
			Assert.Equal(TargetBaseDurability, _resources.GetValue(logic.World, TargetCountryId, ResourceDefinitions.Durability), 6);
		}
	}
}
