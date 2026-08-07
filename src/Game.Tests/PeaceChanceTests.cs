using GS.Game.Configs;
using GS.Game.Systems;
using Xunit;

namespace GS.Game.Tests {
	public class PeaceChanceTests {
		readonly ResourceQuery _resources = new ResourceQuery();
		readonly CountryRelations _relations = new CountryRelations();
		static GameSettings DefaultSettings() => new GameSettings {
			PeaceMinLoseBand = 20,
			PeaceMinWinBand = 20,
			PeaceChanceMinPercent = 1,
			PeaceChanceMaxPercent = 100,
		};

		[Fact]
		void band_exterior_returns_zero() {
			var settings = DefaultSettings();

			Assert.Equal(0.0, Wars.ComputePeaceChancePercent(-79.999, settings));
			Assert.Equal(0.0, Wars.ComputePeaceChancePercent(0.0, settings));
			Assert.Equal(0.0, Wars.ComputePeaceChancePercent(79.999, settings));
		}

		[Fact]
		void progress_zero_returns_zero() {
			Assert.Equal(0.0, Wars.ComputePeaceChancePercent(0.0, DefaultSettings()));
		}

		[Fact]
		void lose_and_win_band_edges_return_min_percent() {
			var settings = DefaultSettings();

			Assert.Equal(1.0, Wars.ComputePeaceChancePercent(-80.0, settings));
			Assert.Equal(1.0, Wars.ComputePeaceChancePercent(80.0, settings));
		}

		[Fact]
		void extremes_return_max_percent() {
			var settings = DefaultSettings();

			Assert.Equal(100.0, Wars.ComputePeaceChancePercent(-100.0, settings));
			Assert.Equal(100.0, Wars.ComputePeaceChancePercent(100.0, settings));
		}

		[Fact]
		void mid_band_interpolates_linearly() {
			var settings = DefaultSettings();

			// Mid lose band: progress = -90 → t = 0.5 → 50.5%
			Assert.Equal(50.5, Wars.ComputePeaceChancePercent(-90.0, settings), precision: 10);
			// Mid win band: progress = 90 → t = 0.5 → 50.5%
			Assert.Equal(50.5, Wars.ComputePeaceChancePercent(90.0, settings), precision: 10);
		}
	}
}
