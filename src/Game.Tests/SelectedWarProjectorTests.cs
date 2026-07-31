using System;
using System.Collections.Generic;
using System.ComponentModel;
using ECS;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class SelectedWarProjectorTests {
		static readonly DateTime Start = new DateTime(1880, 1, 1);

		static void AddResource(World world, string ownerId, OwnerType ownerType, string resourceId, double value) {
			int entity = world.Create();
			world.Add(entity, new ResourceOwner(ownerId, ownerType));
			world.Add(entity, new Resource { ResourceId = resourceId, Value = value });
		}

		static string DeclareWar(World world, string attackerId, string defenderId, double progress = 0) {
			Wars.DeclareWar(world, attackerId, defenderId, Start);
			string warId = GetSingle<War>(world).WarId;
			if (progress != 0) {
				SetWarProgress(world, warId, progress);
			}
			return warId;
		}

		static void SetWarProgress(World world, string warId, double progress) {
			int[] required = { TypeId<ResourceOwner>.Value, TypeId<Resource>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == warId && resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						resources[i].Value = progress;
						return;
					}
				}
			}
			throw new InvalidOperationException($"War progress resource for '{warId}' not found.");
		}

		static int AddWarProgressHistory(World world, string warId, List<ResourceChangeEntry> entries) {
			int[] required = {
				TypeId<ResourceOwner>.Value,
				TypeId<Resource>.Value,
				TypeId<ResourceHistory>.Value
			};
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				ResourceOwner[] owners = arch.GetColumn<ResourceOwner>();
				Resource[] resources = arch.GetColumn<Resource>();
				for (int i = 0; i < arch.Count; i++) {
					if (owners[i].OwnerId == warId && resources[i].ResourceId == ResourceDefinitions.WarProgress) {
						ref ResourceHistory history = ref arch.GetColumn<ResourceHistory>()[i];
						history.History = entries;
						return arch.Entities[i];
					}
				}
			}
			throw new InvalidOperationException($"War progress history for '{warId}' not found.");
		}

		static int AddBattle(
			World world,
			string warId,
			string battleId,
			string provinceId,
			BattleState state,
			int creationSequence,
			DateTime createdAt,
			WarParticipantKind winner = default) {
			int entity = world.Create();
			world.Add(entity, new Battle {
				BattleId = battleId,
				WarId = warId,
				TargetProvinceId = provinceId,
				State = state,
				Winner = winner,
				CreationSequence = creationSequence,
				CreatedAt = createdAt
			});
			return entity;
		}

		static void AddForce(
			World world,
			string battleId,
			string countryId,
			WarParticipantKind side,
			double troops,
			double casualties = 0) {
			int entity = world.Create();
			world.Add(entity, new BattleForce {
				BattleId = battleId,
				CountryId = countryId,
				Side = side,
				Troops = troops,
				Casualties = casualties
			});
		}

		static T GetSingle<T>(World world) {
			int[] required = { TypeId<T>.Value };
			foreach (Archetype arch in world.GetMatchingArchetypes(required, null)) {
				if (arch.Count > 0) {
					return arch.GetColumn<T>()[0];
				}
			}
			throw new InvalidOperationException($"No {typeof(T).Name} entity found.");
		}

		static SelectedWarState Project(World world, SelectedWarState state) {
			SelectedWarProjector.Project(world, state);
			return state;
		}

		[Fact]
		void empty_history_projects_no_entries() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			Assert.True(state.IsValid);
			Assert.Empty(state.History);
		}

		[Fact]
		void history_is_projected_oldest_to_newest() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			var first = new DateTime(1880, 1, 1);
			var second = new DateTime(1880, 2, 1);
			AddWarProgressHistory(world, warId, new List<ResourceChangeEntry> {
				new ResourceChangeEntry { EffectId = "war_progress_decay", AppliedDelta = -1, Timestamp = first },
				new ResourceChangeEntry { EffectId = "war_progress_battle_b1", AppliedDelta = 10, Timestamp = second }
			});
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			Assert.Equal(2, state.History.Count);
			Assert.Equal("war_progress_decay", state.History[0].EffectId);
			Assert.Equal(-1, state.History[0].AppliedDelta);
			Assert.Equal(first, state.History[0].Timestamp);
			Assert.Equal("war_progress_battle_b1", state.History[1].EffectId);
			Assert.Equal(10, state.History[1].AppliedDelta);
			Assert.Equal(second, state.History[1].Timestamp);
		}

		[Fact]
		void side_stats_include_zeros_when_no_battles_or_resources() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			Assert.Equal("France", state.Attacker.CountryId);
			Assert.Equal(0, state.Attacker.RecruitsAvailable);
			Assert.Equal(0, state.Attacker.TroopsInBattles);
			Assert.Equal(0, state.Attacker.Casualties);
			Assert.Equal(0, state.Attacker.Damage);
			Assert.Equal(0, state.Attacker.Durability);
			Assert.Equal("Germany", state.Defender.CountryId);
			Assert.Equal(0, state.Defender.TroopsInBattles);
		}

		[Fact]
		void side_stats_aggregate_country_resources_and_battle_forces() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.Recruits, 50);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.Damage, 12);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.Durability, 34);
			AddResource(world, "Germany", OwnerType.Country, ResourceDefinitions.Recruits, 70);
			AddBattle(world, warId, "active", "France__west", BattleState.Active, 1, Start.AddHours(1));
			AddForce(world, "active", "France", WarParticipantKind.Attacker, 20);
			AddForce(world, "active", "Germany", WarParticipantKind.Defender, 5);
			AddBattle(world, warId, "finished", "Germany__east", BattleState.Finished, 2, Start.AddHours(2), WarParticipantKind.Attacker);
			AddForce(world, "finished", "France", WarParticipantKind.Attacker, 0, 3);
			AddForce(world, "finished", "Germany", WarParticipantKind.Defender, 0, 7);
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			Assert.Equal(50, state.Attacker.RecruitsAvailable);
			Assert.Equal(20, state.Attacker.TroopsInBattles);
			Assert.Equal(3, state.Attacker.Casualties);
			Assert.Equal(12, state.Attacker.Damage);
			Assert.Equal(34, state.Attacker.Durability);
			Assert.Equal(70, state.Defender.RecruitsAvailable);
			Assert.Equal(5, state.Defender.TroopsInBattles);
			Assert.Equal(7, state.Defender.Casualties);
		}

		[Fact]
		void battles_are_ordered_by_creation_sequence() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			AddBattle(world, warId, "z_battle", "Z", BattleState.Active, 2, Start.AddHours(2));
			AddBattle(world, warId, "a_battle", "A", BattleState.Active, 1, Start.AddHours(1));
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			Assert.Equal(2, state.Battles.Count);
			Assert.Equal("a_battle", state.Battles[0].BattleId);
			Assert.Equal("z_battle", state.Battles[1].BattleId);
		}

		[Theory]
		[InlineData(100, 0, 100)]
		[InlineData(0, 100, -100)]
		[InlineData(75, 25, 50)]
		[InlineData(0, 0, 0)]
		public void active_battle_progress_formula(double attackerTroops, double defenderTroops, double expected) {
			Assert.Equal(expected, SelectedWarProjector.ComputeActiveBattleProgress(attackerTroops, defenderTroops));
		}

		[Fact]
		void active_battle_row_includes_progress_and_troops() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			AddBattle(world, warId, "active", "France__west", BattleState.Active, 1, Start);
			AddForce(world, "active", "France", WarParticipantKind.Attacker, 60);
			AddForce(world, "active", "Germany", WarParticipantKind.Defender, 40);
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			WarBattleRowState row = Assert.Single(state.Battles);
			Assert.False(row.IsFinished);
			Assert.Equal(20, row.Progress);
			Assert.Equal(60, row.AttackerTroops);
			Assert.Equal(40, row.DefenderTroops);
		}

		[Fact]
		void finished_battle_row_keeps_attacker_first_casualty_payload() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			AddBattle(world, warId, "finished", "France__west", BattleState.Finished, 1, Start, WarParticipantKind.Defender);
			AddForce(world, "finished", "France", WarParticipantKind.Attacker, 0, 11);
			AddForce(world, "finished", "Germany", WarParticipantKind.Defender, 0, 22);
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			Project(world, state);

			WarBattleRowState row = Assert.Single(state.Battles);
			Assert.True(row.IsFinished);
			Assert.Equal("Germany", row.WinnerCountryId);
			Assert.Equal(WarParticipantKind.Defender, row.WinnerSide);
			Assert.Equal(11, row.AttackerCasualties);
			Assert.Equal(22, row.DefenderCasualties);
		}

		[Fact]
		void clearing_selection_invalidates_state() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			var state = new SelectedWarState();
			state.RequestOpen(warId);
			Project(world, state);
			Assert.True(state.IsValid);

			state.Clear();
			Project(world, state);

			Assert.False(state.IsValid);
			Assert.Equal("", state.WarId);
		}

		[Fact]
		void identical_set_does_not_notify() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany", 5);
			var state = new SelectedWarState();
			state.RequestOpen(warId);
			int notifications = 0;
			state.PropertyChanged += (_, __) => notifications++;

			Project(world, state);
			Assert.Equal(1, notifications);

			Project(world, state);
			Assert.Equal(1, notifications);

			SetWarProgress(world, warId, 6);
			Project(world, state);
			Assert.Equal(2, notifications);
		}

		static void AddCharacterWithSkill(World world, string characterId, string countryId, string roleId, string skillId, double skillValue) {
			int charEntity = world.Create();
			world.Add(charEntity, new Character {
				CharacterId = characterId,
				CountryId = countryId,
				OrgId = "",
				RoleId = roleId,
				NamePartKeys = Array.Empty<string>()
			});
			AddSkill(world, characterId, skillId, skillValue);
		}

		static void AddSkill(World world, string characterId, string skillId, double skillValue) {
			int skillEntity = world.Create();
			world.Add(skillEntity, new ResourceOwner(characterId, OwnerType.Character));
			world.Add(skillEntity, new Resource { ResourceId = skillId, Value = skillValue });
		}

		[Fact]
		void side_stats_include_damage_and_durability_composition() {
			var world = new World();
			string warId = DeclareWar(world, "France", "Germany");
			AddCharacterWithSkill(world, "ruler_1", "France", "ruler", "power", 20);
			AddCharacterWithSkill(world, "mil_1", "France", "military_advisor", "power", 15);
			AddSkill(world, "ruler_1", "stinginess", 5);
			AddCharacterWithSkill(world, "econ_1", "France", "economic_advisor", "stinginess", 8);
			AddResource(world, "France", OwnerType.Country, ResourceDefinitions.TroopsDamageBonusPercent, 10);
			int orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "org_a", DisplayName = "Illuminati" });
			int decayEffect = world.Create();
			world.Add(decayEffect, new ResourceOwner("France", OwnerType.Country));
			world.Add(decayEffect, new ResourceLink(ResourceDefinitions.TroopsDamageBonusPercent));
			world.Add(decayEffect, new ResourceEffect {
				EffectId = "country_resource_decay_sell_arms_damage_bonus_effect_org_a_France_1_1",
				Value = -1,
				PayType = PayType.Monthly,
				MaxTotal = 10,
				OrgId = "org_a"
			});
			var countryConfig = new CountryConfig {
				Countries = new List<CountryEntry> {
					new CountryEntry { CountryId = "France", BaseDamage = 85, BaseDurability = 60 }
				}
			};
			var state = new SelectedWarState();
			state.RequestOpen(warId);

			SelectedWarProjector.Project(world, state, countryConfig);

			Assert.Equal(85, state.Attacker.DamageBase);
			Assert.Equal(20, state.Attacker.DamageRulerBonus);
			Assert.Equal(15, state.Attacker.DamageAdvisorBonus);
			Assert.Equal(10, state.Attacker.DamageBonusPercent);
			EffectStateEntry bonusEffect = Assert.Single(state.Attacker.DamageBonusEffects);
			Assert.Equal(10, bonusEffect.MaxTotal);
			Assert.Equal(-1, bonusEffect.Value);
			Assert.Equal("Illuminati", bonusEffect.OrgDisplayName);
			Assert.Equal(60, state.Attacker.DurabilityBase);
			Assert.Equal(5, state.Attacker.DurabilityRulerBonus);
			Assert.Equal(8, state.Attacker.DurabilityAdvisorBonus);
		}

		[Fact]
		void missing_war_invalidates_pending_selection() {
			var world = new World();
			var state = new SelectedWarState();
			state.RequestOpen("missing_war");

			Project(world, state);

			Assert.False(state.IsValid);
		}
	}
}
