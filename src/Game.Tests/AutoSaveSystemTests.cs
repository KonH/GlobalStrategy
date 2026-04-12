using System;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;
using Xunit;

namespace GS.Game.Tests {
	public class AutoSaveSystemTests {
		static readonly DateTime Base = new DateTime(1880, 1, 15, 0, 0, 0);

		(World world, int settings, int time, FakeCommandAccessor commands) Setup(
			AutoSaveInterval interval, DateTime current) {
			var world = new World();
			int se = world.Create();
			world.Add(se, new AppSettings { Locale = "en", AutoSaveInterval = interval });
			int te = world.Create();
			world.Add(te, new GameTime { CurrentTime = current, IsPaused = false });
			return (world, se, te, new FakeCommandAccessor());
		}

		[Fact]
		void daily_no_save_same_day() {
			var (world, se, te, commands) = Setup(AutoSaveInterval.Daily, Base);
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(0, commands.SaveCount);
		}

		[Fact]
		void daily_saves_when_day_crosses() {
			var (world, se, te, commands) = Setup(AutoSaveInterval.Daily, Base.AddDays(1));
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(1, commands.SaveCount);
		}

		[Fact]
		void monthly_no_save_same_month() {
			var (world, se, te, commands) = Setup(AutoSaveInterval.Monthly, Base.AddDays(10));
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(0, commands.SaveCount);
		}

		[Fact]
		void monthly_saves_when_month_crosses() {
			var nextMonth = new DateTime(1880, 2, 1);
			var (world, se, te, commands) = Setup(AutoSaveInterval.Monthly, nextMonth);
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(1, commands.SaveCount);
		}

		[Fact]
		void yearly_no_save_same_year() {
			var (world, se, te, commands) = Setup(AutoSaveInterval.Yearly, new DateTime(1880, 12, 31));
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(0, commands.SaveCount);
		}

		[Fact]
		void yearly_saves_when_year_crosses() {
			var (world, se, te, commands) = Setup(AutoSaveInterval.Yearly, new DateTime(1881, 1, 1));
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(1, commands.SaveCount);
		}

		[Fact]
		void no_save_when_paused() {
			var world = new World();
			int se = world.Create();
			world.Add(se, new AppSettings { Locale = "en", AutoSaveInterval = AutoSaveInterval.Daily });
			int te = world.Create();
			world.Add(te, new GameTime { CurrentTime = Base.AddDays(1), IsPaused = true });
			var commands = new FakeCommandAccessor();
			AutoSaveSystem.Update(world, se, te, Base, commands);
			Assert.Equal(0, commands.SaveCount);
		}

		sealed class FakeCommandAccessor : IWriteOnlyCommandAccessor {
			public int SaveCount { get; private set; }
			public void Push<TCommand>(TCommand cmd) where TCommand : ICommand {
				if (cmd is SaveGameCommand) {
					SaveCount++;
				}
			}
		}
	}
}
