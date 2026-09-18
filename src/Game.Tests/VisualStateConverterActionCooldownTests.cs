using System;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Game.Systems;
using GS.Game.Tests.Helpers;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class VisualStateConverterActionCooldownTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static readonly DateTime BaseTime = new DateTime(1880, 6, 1);

		static ActionConfig BuildActionConfig(double cooldownDays = 7) {
			return TestActionConfig.Create()
				.HandSize("country", 3)
				.Action("declare_war", cooldownDays: cooldownDays)
				.Build();
		}

		static TestWorld BuildWorldWithCard(DateTime currentTime) {
			return TestWorld.CreateWithSelectedCountry(time: currentTime)
				.Card("declare_war");
		}

		static void AddCooldownTracking(TestWorld world, string orgId, string actionId, DateTime endTime) {
			world.Entity().With(new ActionCooldownState { OrgId = orgId, ActionId = actionId, EndTime = endTime });
		}

		VisualStateProbe Probe(ActionConfig config) {
			return new VisualStateProbe(config, _resources, _relations);
		}

		[Fact]
		void card_on_cooldown_is_unplayable_with_on_cooldown_reason_and_matching_remaining_values() {
			ActionConfig config = BuildActionConfig();
			TestWorld world = BuildWorldWithCard(BaseTime);
			DateTime endTime = BaseTime.AddDays(3);
			AddCooldownTracking(world, "OrgA", "declare_war", endTime);

			VisualStateProbe probe = Probe(config).Update(world);

			ActionCardEntry entry = probe.Card("declare_war");
			Assert.True(entry.IsUnplayable);
			Assert.Equal("on_cooldown", entry.UnplayableReason);
			Assert.NotNull(entry.CooldownRemainingDays);
			Assert.Equal(Math.Ceiling((endTime - BaseTime).TotalDays), entry.CooldownRemainingDays!.Value);
			Assert.NotNull(entry.CooldownFractionRemaining);
			Assert.InRange(entry.CooldownFractionRemaining!.Value, 0.0, 1.0);
		}

		[Fact]
		void card_with_no_cooldown_tracking_entity_has_null_cooldown_fields() {
			TestWorld world = BuildWorldWithCard(BaseTime);

			VisualStateProbe probe = Probe(BuildActionConfig()).Update(world);

			ActionCardEntry entry = probe.Card("declare_war");
			Assert.Null(entry.CooldownRemainingDays);
			Assert.Null(entry.CooldownFractionRemaining);
		}

		[Fact]
		void cooldown_fraction_remaining_is_one_at_instant_of_play() {
			ActionConfig config = BuildActionConfig(cooldownDays: 30);
			TestWorld world = BuildWorldWithCard(BaseTime);
			AddCooldownTracking(world, "OrgA", "declare_war", BaseTime.AddDays(30));

			VisualStateProbe probe = Probe(config).Update(world);

			Assert.Equal(1.0, probe.Card("declare_war").CooldownFractionRemaining!.Value);
		}

		[Fact]
		void cooldown_fraction_remaining_approaches_zero_just_before_expiry() {
			ActionConfig config = BuildActionConfig();
			TestWorld world = BuildWorldWithCard(BaseTime);
			AddCooldownTracking(world, "OrgA", "declare_war", BaseTime.AddMinutes(1));

			VisualStateProbe probe = Probe(config).Update(world);

			ActionCardEntry entry = probe.Card("declare_war");
			Assert.NotNull(entry.CooldownFractionRemaining);
			Assert.True(entry.CooldownFractionRemaining!.Value < 0.01);
			Assert.True(entry.CooldownFractionRemaining!.Value >= 0.0);
		}
	}
}
