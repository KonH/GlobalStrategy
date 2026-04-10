using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class SelectCountrySystem {
		public static void Update(World world, ReadCommands<SelectCountryCommand> commands) {
			if (commands.Count == 0) {
				return;
			}
			var span = commands.AsSpan();
			string targetId = span[span.Length - 1].CountryId;

			// Collect before modifying to avoid archetype-iteration issues
			var toSelect = new List<int>();
			var toDeselect = new List<int>();

			world.Query<Country>((int e, ref Country c) => {
				if (c.CountryId == targetId) {
					toSelect.Add(e);
				} else if (world.Has<IsSelected>(e)) {
					toDeselect.Add(e);
				}
			});

			foreach (int e in toSelect) {
				if (!world.Has<IsSelected>(e)) {
					world.Add(e, new IsSelected());
				}
			}
			foreach (int e in toDeselect) {
				world.Remove<IsSelected>(e);
			}
		}
	}
}
