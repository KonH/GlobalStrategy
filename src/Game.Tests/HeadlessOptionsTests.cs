using System;
using GS.Game.ConsoleRunner;
using Xunit;

namespace GS.Game.Tests {
	public class HeadlessOptionsTests {
		[Fact]
		void hours_per_tick_above_672_is_rejected() {
			Assert.Throws<ArgumentException>(() => HeadlessOptions.Parse(new[] {
				"--headless", "--seed", "1", "--output", "out.json", "--max-ticks", "10", "--hours-per-tick", "673"
			}));
		}

		[Fact]
		void hours_per_tick_zero_is_rejected() {
			Assert.Throws<ArgumentException>(() => HeadlessOptions.Parse(new[] {
				"--headless", "--seed", "1", "--output", "out.json", "--max-ticks", "10", "--hours-per-tick", "0"
			}));
		}

		[Fact]
		void headless_requires_seed_output_and_a_stop_condition() {
			Assert.Throws<ArgumentException>(() => HeadlessOptions.Parse(new[] { "--headless", "--output", "out.json", "--max-ticks", "10" }));
			Assert.Throws<ArgumentException>(() => HeadlessOptions.Parse(new[] { "--headless", "--seed", "1", "--max-ticks", "10" }));
			Assert.Throws<ArgumentException>(() => HeadlessOptions.Parse(new[] { "--headless", "--seed", "1", "--output", "out.json" }));
		}

		[Fact]
		void orgs_flag_parses_comma_separated_list() {
			var options = HeadlessOptions.Parse(new[] {
				"--headless", "--seed", "1", "--output", "out.json", "--max-ticks", "10", "--orgs", "Illuminati,Masons"
			});
			Assert.NotNull(options.OrgIds);
			Assert.Equal(new[] { "Illuminati", "Masons" }, options.OrgIds);
		}

		[Fact]
		void defaults_are_applied() {
			var options = HeadlessOptions.Parse(Array.Empty<string>());
			Assert.False(options.IsHeadless);
			Assert.Equal(24, options.HoursPerTick);
			Assert.Equal(300, options.TimeoutSeconds);
			Assert.Equal("Assets/Configs", options.ConfigDir);
		}
	}
}
