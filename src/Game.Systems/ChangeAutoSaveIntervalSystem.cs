using System;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class ChangeAutoSaveIntervalSystem {
		public static void Update(
			World world,
			int settingsEntity,
			ReadCommands<ChangeAutoSaveIntervalCommand> commands) {
			if (commands.Count == 0) {
				return;
			}
			var span = commands.AsSpan();
			string intervalStr = span[span.Length - 1].Interval;

			AutoSaveInterval parsed = intervalStr.ToLowerInvariant() switch {
				"daily" => AutoSaveInterval.Daily,
				"yearly" => AutoSaveInterval.Yearly,
				_ => AutoSaveInterval.Monthly
			};

			ref AppSettings settings = ref world.Get<AppSettings>(settingsEntity);
			settings.AutoSaveInterval = parsed;
		}
	}
}
