using System;
using GS.Game.ConsoleRunner;
using Xunit;

namespace GS.Game.Tests {
	public class CalibrationOptionsTests {
		static readonly string[] BaseArgs = {
			"calibrate-end-game", "--scenario", "win", "--org", "Illuminati", "--seed", "1", "--output", "out.json"
		};

		[Fact]
		void hours_per_tick_above_672_is_rejected() {
			Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--scenario", "win", "--org", "Illuminati", "--seed", "1", "--output", "out.json",
				"--hours-per-tick", "673"
			}));
		}

		[Fact]
		void hours_per_tick_zero_is_rejected() {
			Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--scenario", "win", "--org", "Illuminati", "--seed", "1", "--output", "out.json",
				"--hours-per-tick", "0"
			}));
		}

		[Fact]
		void missing_scenario_is_rejected_with_a_clear_message() {
			var exception = Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--org", "Illuminati", "--seed", "1", "--output", "out.json"
			}));
			Assert.Contains("requires --scenario", exception.Message);
		}

		[Fact]
		void invalid_scenario_is_rejected_with_the_allowed_values() {
			var exception = Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--scenario", "draw", "--org", "Illuminati", "--seed", "1", "--output", "out.json"
			}));
			Assert.Contains("must be 'win' or 'lose'", exception.Message);
		}

		[Fact]
		void requires_org_seed_and_output() {
			Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--scenario", "win", "--seed", "1", "--output", "out.json"
			}));
			Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--scenario", "win", "--org", "Illuminati", "--output", "out.json"
			}));
			Assert.Throws<ArgumentException>(() => CalibrationOptions.Parse(new[] {
				"calibrate-end-game", "--scenario", "win", "--org", "Illuminati", "--seed", "1"
			}));
		}

		[Fact]
		void defaults_are_applied() {
			var options = CalibrationOptions.Parse(BaseArgs);
			Assert.Equal(24, options.HoursPerTick);
			Assert.Equal(300, options.TimeoutSeconds);
			Assert.Equal(20000, options.MaxTicks);
			Assert.Equal("Assets/Configs", options.ConfigDir);
		}
	}
}
