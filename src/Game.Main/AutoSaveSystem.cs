using System;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Main {
	public static class AutoSaveSystem {
		public static void Update(
			World world,
			int settingsEntity,
			int gameTimeEntity,
			DateTime previousTime,
			IWriteOnlyCommandAccessor commands) {
			ref AppSettings settings = ref world.Get<AppSettings>(settingsEntity);
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);

			if (time.IsPaused) {
				return;
			}

			bool shouldSave = settings.AutoSaveInterval switch {
				AutoSaveInterval.Daily   => time.CurrentTime.Date > previousTime.Date,
				AutoSaveInterval.Monthly => DifferentMonth(previousTime, time.CurrentTime),
				AutoSaveInterval.Yearly  => time.CurrentTime.Year > previousTime.Year,
				_                        => false
			};

			if (shouldSave) {
				commands.Push(new SaveGameCommand());
			}
		}

		static bool DifferentMonth(DateTime a, DateTime b) =>
			b.Year > a.Year || (b.Year == a.Year && b.Month > a.Month);
	}
}
