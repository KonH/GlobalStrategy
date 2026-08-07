using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterActionCooldownTests {
		static readonly DateTime BaseTime = new DateTime(1880, 6, 1);

		static ActionConfig BuildActionConfig(double cooldownDays = 7) {
			return new ActionConfig {
				Defaults = new List<ActionOwnerDefaults> {
					new ActionOwnerDefaults { OwnerType = "country", HandSize = 3 }
				},
				Actions = new List<ActionDefinition> {
					new ActionDefinition { ActionId = "declare_war", OwnerType = "country", CooldownDays = cooldownDays }
				}
			};
		}

		static World BuildWorldWithCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, DateTime currentTime) {
			var world = new World();
			int countryEntity = world.Create();
			world.Add(countryEntity, new Country("Prussia"));
			world.Add(countryEntity, new IsSelected());

			orgEntity = world.Create();
			world.Add(orgEntity, new Organization { OrganizationId = "OrgA", DisplayName = "OrgA" });

			gameTimeEntity = world.Create();
			world.Add(gameTimeEntity, new GameTime { CurrentTime = currentTime, IsPaused = false, MultiplierIndex = 0 });

			localeEntity = world.Create();
			world.Add(localeEntity, new Locale { Value = "en" });

			int cardEntity = world.Create();
			world.Add(cardEntity, new GameAction { ActionId = "declare_war" });
			world.Add(cardEntity, new OrgContext { OrgId = "OrgA" });
			world.Add(cardEntity, new CardOwnerType(CardOwnerKind.Country));
			world.Add(cardEntity, new CountryContext { CountryId = "Prussia" });
			world.Add(cardEntity, new CardInHand { SlotIndex = 0 });

			return world;
		}

		static void AddCooldownTracking(World world, string orgId, string actionId, DateTime endTime) {
			int e = world.Create();
			world.Add(e, new ActionCooldownState { OrgId = orgId, ActionId = actionId, EndTime = endTime });
		}

		static ActionCardEntry? FindEntry(IReadOnlyList<ActionCardEntry> entries, string actionId) {
			foreach (var e in entries) {
				if (e.ActionId == actionId) { return e; }
			}
			return null;
		}

		[Fact]
		void card_on_cooldown_is_unplayable_with_on_cooldown_reason_and_matching_remaining_values() {
			var config = BuildActionConfig();
			var world = BuildWorldWithCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, BaseTime);
			DateTime endTime = BaseTime.AddDays(3);
			AddCooldownTracking(world, "OrgA", "declare_war", endTime);

			var state = new VisualState();
			var converter = new VisualStateConverter(state, config);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_war");
			Assert.NotNull(entry);
			Assert.True(entry!.IsUnplayable);
			Assert.Equal("on_cooldown", entry.UnplayableReason);
			Assert.NotNull(entry.CooldownRemainingDays);
			Assert.Equal(Math.Ceiling((endTime - BaseTime).TotalDays), entry.CooldownRemainingDays!.Value);
			Assert.NotNull(entry.CooldownFractionRemaining);
			Assert.InRange(entry.CooldownFractionRemaining!.Value, 0.0, 1.0);
		}

		[Fact]
		void card_with_no_cooldown_tracking_entity_has_null_cooldown_fields() {
			var config = BuildActionConfig();
			var world = BuildWorldWithCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, BaseTime);

			var state = new VisualState();
			var converter = new VisualStateConverter(state, config);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_war");
			Assert.NotNull(entry);
			Assert.Null(entry!.CooldownRemainingDays);
			Assert.Null(entry.CooldownFractionRemaining);
		}

		[Fact]
		void cooldown_fraction_remaining_is_one_at_instant_of_play() {
			var config = BuildActionConfig(cooldownDays: 30);
			var world = BuildWorldWithCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, BaseTime);
			DateTime endTime = BaseTime.AddDays(30);
			AddCooldownTracking(world, "OrgA", "declare_war", endTime);

			var state = new VisualState();
			var converter = new VisualStateConverter(state, config);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_war");
			Assert.NotNull(entry);
			Assert.Equal(1.0, entry!.CooldownFractionRemaining!.Value);
		}

		[Fact]
		void cooldown_fraction_remaining_approaches_zero_just_before_expiry() {
			var config = BuildActionConfig();
			DateTime endTime = BaseTime.AddMinutes(1);
			var world = BuildWorldWithCard(out int gameTimeEntity, out int localeEntity, out int orgEntity, BaseTime);
			AddCooldownTracking(world, "OrgA", "declare_war", endTime);

			var state = new VisualState();
			var converter = new VisualStateConverter(state, config);

			converter.Update(0f, world, gameTimeEntity, localeEntity, orgEntity);

			var entry = FindEntry(state.SelectedCountry.CountryActions.Hand, "declare_war");
			Assert.NotNull(entry);
			Assert.NotNull(entry!.CooldownFractionRemaining);
			Assert.True(entry.CooldownFractionRemaining!.Value < 0.01);
			Assert.True(entry.CooldownFractionRemaining!.Value >= 0.0);
		}
	}
}
