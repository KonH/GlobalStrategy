using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class LocaleSystem {
		public static void Update(World world, int localeEntity, ReadCommands<ChangeLocaleCommand> commands) {
			if (commands.Count == 0) {
				return;
			}
			var span = commands.AsSpan();
			ref Locale locale = ref world.Get<Locale>(localeEntity);
			locale.Value = span[span.Length - 1].Locale;
		}
	}
}
