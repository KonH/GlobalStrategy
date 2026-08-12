using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class TimeSystem {
		public static void Update(
			World world,
			int gameTimeEntity,
			float deltaTime,
			int[] speedMultipliers,
			ReadCommands<PauseCommand> pause,
			ReadCommands<UnpauseCommand> unpause,
			ReadCommands<ChangeTimeMultiplierCommand> changeSpeed) {
			ref GameTime time = ref world.Get<GameTime>(gameTimeEntity);
			if (pause.Count > 0) {
				time.IsPaused = true;
			}
			if (unpause.Count > 0) {
				time.IsPaused = false;
				ClearTutorialOwnsPause(world, gameTimeEntity);
			}
			var span = changeSpeed.AsSpan();
			if (span.Length > 0) {
				time.MultiplierIndex = span[span.Length - 1].Index;
				if (time.IsPaused) {
					time.IsPaused = false;
					ClearTutorialOwnsPause(world, gameTimeEntity);
				}
			}
			if (time.IsPaused) {
				return;
			}
			time.AccumulatedHours += deltaTime * speedMultipliers[time.MultiplierIndex];
			int hours = (int)time.AccumulatedHours;
			if (hours > 0) {
				time.CurrentTime = time.CurrentTime.AddHours(hours);
				time.AccumulatedHours -= hours;
			}
		}

		static void ClearTutorialOwnsPause(World world, int gameTimeEntity) {
			if (world.Has<TutorialOwnsPause>(gameTimeEntity)) {
				world.Remove<TutorialOwnsPause>(gameTimeEntity);
			}
		}
	}
}
