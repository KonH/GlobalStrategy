using System;
using System.IO;
using GS.Configs.IO;
using GS.Game.Common;
using GS.Game.Configs;
using Xunit;

namespace GS.Game.Tests {
	public class StringConfigParityTests {
		// dotnet test runs from the test assembly's own output directory, not the repo root -
		// walk up until Assets/Configs is found, same pattern as BotActionLogEncodingTests.
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
		void geojson_config_parity() {
			string path = FindRepoRootConfigPath("geojson_world.json");
			var fromFile = new FileConfig<GeoJsonConfig>(path).Load();
			var fromString = new StringConfig<GeoJsonConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Features.Count, fromString.Features.Count);
			Assert.Equal(fromFile.Features[0].Name, fromString.Features[0].Name);
		}

		[Fact]
		void map_entry_config_parity() {
			string path = FindRepoRootConfigPath("map_entry_config.json");
			var fromFile = new FileConfig<MapEntryConfig>(path).Load();
			var fromString = new StringConfig<MapEntryConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Features.Count, fromString.Features.Count);
			Assert.Equal(fromFile.Features[0].MapFeatureId, fromString.Features[0].MapFeatureId);
		}

		[Fact]
		void country_config_parity() {
			string path = FindRepoRootConfigPath("country_config.json");
			var fromFile = new FileConfig<CountryConfig>(path).Load();
			var fromString = new StringConfig<CountryConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Countries.Count, fromString.Countries.Count);
			Assert.Equal(fromFile.Countries[0].CountryId, fromString.Countries[0].CountryId);
			Assert.Equal(fromFile.Countries[0].IsAvailable, fromString.Countries[0].IsAvailable);
		}

		[Fact]
		void game_settings_parity() {
			string path = FindRepoRootConfigPath("game_settings.json");
			var fromFile = new FileConfig<GameSettings>(path).Load();
			var fromString = new StringConfig<GameSettings>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.StartYear, fromString.StartYear);
			Assert.Equal(fromFile.DefaultLocale, fromString.DefaultLocale);
			Assert.Equal(fromFile.SpeedMultipliers, fromString.SpeedMultipliers);
			Assert.Equal(50, fromFile.DiscardGoldCost);
			Assert.Equal(fromFile.DiscardGoldCost, fromString.DiscardGoldCost);
		}

		[Fact]
		void resource_config_parity() {
			string path = FindRepoRootConfigPath("resource_config.json");
			var fromFile = new FileConfig<ResourceConfig>(path).Load();
			var fromString = new StringConfig<ResourceConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Resources.Count, fromString.Resources.Count);
			Assert.Equal(fromFile.Resources[0].ResourceId, fromString.Resources[0].ResourceId);
			Assert.Equal(fromFile.Resources[0].SeedTarget, fromString.Resources[0].SeedTarget);

			ResourceDefinition? modifierFromFile = fromFile.FindResource(ResourceDefinitions.TroopsDamageBonusPercent);
			ResourceDefinition? modifierFromString = fromString.FindResource(ResourceDefinitions.TroopsDamageBonusPercent);
			Assert.NotNull(modifierFromFile);
			Assert.NotNull(modifierFromString);
			Assert.Equal(ResourceSeedTarget.Country, modifierFromFile.SeedTarget);
			Assert.Equal(ResourceSeedTarget.Country, modifierFromString.SeedTarget);
			Assert.Equal(0.0, modifierFromFile.DefaultInitialValue);
			Assert.Equal(0.0, modifierFromString.DefaultInitialValue);
			Assert.Empty(modifierFromFile.DefaultEffects);
			Assert.Empty(modifierFromString.DefaultEffects);
			Assert.DoesNotContain(ResourceDefinitions.TroopsDamageBonusPercent, fromFile.DisplayWhitelist);
			Assert.DoesNotContain(ResourceDefinitions.TroopsDamageBonusPercent, fromString.DisplayWhitelist);
		}

		[Fact]
		void organization_config_parity() {
			string path = FindRepoRootConfigPath("organizations.json");
			var fromFile = new FileConfig<OrganizationConfig>(path).Load();
			var fromString = new StringConfig<OrganizationConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Organizations.Count, fromString.Organizations.Count);
			Assert.Equal(fromFile.Organizations[0].OrganizationId, fromString.Organizations[0].OrganizationId);
			Assert.Equal(fromFile.Organizations[0].InitialGold, fromString.Organizations[0].InitialGold);
		}

		[Fact]
		void character_config_parity() {
			string path = FindRepoRootConfigPath("character_config.json");
			var fromFile = new FileConfig<CharacterConfig>(path).Load();
			var fromString = new StringConfig<CharacterConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Roles.Count, fromString.Roles.Count);
			Assert.Equal(fromFile.CountryPools.Count, fromString.CountryPools.Count);
		}

		[Fact]
		void action_config_parity() {
			string path = FindRepoRootConfigPath("action_config.json");
			var fromFile = new FileConfig<ActionConfig>(path).Load();
			var fromString = new StringConfig<ActionConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Actions.Count, fromString.Actions.Count);
			Assert.Equal(fromFile.Actions[0].ActionId, fromString.Actions[0].ActionId);
			Assert.Equal(fromFile.Actions[0].EffectIds, fromString.Actions[0].EffectIds);
			Assert.Equal(8, fromFile.GetHandSize("country"));
			Assert.Equal(8, fromString.GetHandSize("country"));
		}

		[Fact]
		void sell_arms_action_deserializes_with_required_conditions_and_effects() {
			string path = FindRepoRootConfigPath("action_config.json");
			var fromFile = new FileConfig<ActionConfig>(path).Load();
			var fromString = new StringConfig<ActionConfig>(File.ReadAllText(path)).Load();

			foreach (ActionConfig config in new[] { fromFile, fromString }) {
				ActionDefinition action = Assert.IsType<ActionDefinition>(config.Find("sell_arms"));
				Assert.Equal("country", action.OwnerType);
				Assert.Equal("military_advisor", action.TargetRole);
				Assert.Equal(9, action.DeckCopies);
				Assert.Equal(21, action.CooldownDays);
				ActionCost cost = Assert.Single(action.Cost);
				Assert.Equal("gold", cost.ResourceId);
				Assert.Equal(175.0, cost.Amount);
				Assert.Equal(
					new[] { "sell_arms_damage_bonus_effect" },
					action.EffectIds);
				Assert.Collection(
					action.Conditions,
					condition => {
						Assert.Equal("gte", condition.Type);
						Assert.Equal("opinion", condition.Members[0].Type);
						Assert.Equal(80, condition.Members[1].Value);
					});
			}
		}

		[Fact]
		void effect_config_parity() {
			string path = FindRepoRootConfigPath("effect_config.json");
			var fromFile = new FileConfig<EffectConfig>(path).Load();
			var fromString = new StringConfig<EffectConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Effects.Count, fromString.Effects.Count);
			Assert.Equal(fromFile.Effects[0].EffectId, fromString.Effects[0].EffectId);
			Assert.Equal(fromFile.Effects[0].GetType(), fromString.Effects[0].GetType());
		}

		[Fact]
		void effect_config_set_country_relation_entries_deserialize_with_correct_kind_through_both_sources() {
			string path = FindRepoRootConfigPath("effect_config.json");
			var fromFile = new FileConfig<EffectConfig>(path).Load();
			var fromString = new StringConfig<EffectConfig>(File.ReadAllText(path)).Load();

			var friendFromFile = Assert.IsType<SetCountryRelationEffectParams>(fromFile.Find("make_friend_effect"));
			var friendFromString = Assert.IsType<SetCountryRelationEffectParams>(fromString.Find("make_friend_effect"));
			Assert.Equal(RelationKind.Friend, friendFromFile.Kind);
			Assert.Equal(RelationKind.Friend, friendFromString.Kind);

			var rivalFromFile = Assert.IsType<SetCountryRelationEffectParams>(fromFile.Find("make_rival_effect"));
			var rivalFromString = Assert.IsType<SetCountryRelationEffectParams>(fromString.Find("make_rival_effect"));
			Assert.Equal(RelationKind.Rival, rivalFromFile.Kind);
			Assert.Equal(RelationKind.Rival, rivalFromString.Kind);
		}

		[Fact]
		void effect_config_resolve_war_entries_deserialize_with_correct_outcome_through_both_sources() {
			string path = FindRepoRootConfigPath("effect_config.json");
			var fromFile = new FileConfig<EffectConfig>(path).Load();
			var fromString = new StringConfig<EffectConfig>(File.ReadAllText(path)).Load();

			var ultimatumFromFile = Assert.IsType<ResolveWarEffectParams>(fromFile.Find("force_war_win_effect"));
			var ultimatumFromString = Assert.IsType<ResolveWarEffectParams>(fromString.Find("force_war_win_effect"));
			Assert.Equal(WarOutcome.Win, ultimatumFromFile.Outcome);
			Assert.Equal(WarOutcome.Win, ultimatumFromString.Outcome);

			var surrenderFromFile = Assert.IsType<ResolveWarEffectParams>(fromFile.Find("force_war_loss_effect"));
			var surrenderFromString = Assert.IsType<ResolveWarEffectParams>(fromString.Find("force_war_loss_effect"));
			Assert.Equal(WarOutcome.Lose, surrenderFromFile.Outcome);
			Assert.Equal(WarOutcome.Lose, surrenderFromString.Outcome);
		}

		[Fact]
		void sell_arms_effect_entries_deserialize_with_configured_values_through_both_sources() {
			string path = FindRepoRootConfigPath("effect_config.json");
			var fromFile = new FileConfig<EffectConfig>(path).Load();
			var fromString = new StringConfig<EffectConfig>(File.ReadAllText(path)).Load();

			var modifierFromFile = Assert.IsType<CountryResourceModifierEffectParams>(
				fromFile.Find("sell_arms_damage_bonus_effect"));
			var modifierFromString = Assert.IsType<CountryResourceModifierEffectParams>(
				fromString.Find("sell_arms_damage_bonus_effect"));
			Assert.Equal(ResourceDefinitions.TroopsDamageBonusPercent, modifierFromFile.ResourceId);
			Assert.Equal(ResourceDefinitions.TroopsDamageBonusPercent, modifierFromString.ResourceId);
			Assert.Equal(20.0, modifierFromFile.InitialValue);
			Assert.Equal(20.0, modifierFromString.InitialValue);
			Assert.Equal(1.0, modifierFromFile.DecayPerMonth);
			Assert.Equal(1.0, modifierFromString.DecayPerMonth);
		}

		[Fact]
		void province_config_parity() {
			string path = FindRepoRootConfigPath("province_config.json");
			var fromFile = new FileConfig<ProvinceConfig>(path).Load();
			var fromString = new StringConfig<ProvinceConfig>(File.ReadAllText(path)).Load();
			Assert.Equal(fromFile.Provinces.Count, fromString.Provinces.Count);
			Assert.Equal(fromFile.Provinces[0].ProvinceId, fromString.Provinces[0].ProvinceId);
			Assert.Equal(fromFile.Provinces[0].Population, fromString.Provinces[0].Population);
		}
	}
}
