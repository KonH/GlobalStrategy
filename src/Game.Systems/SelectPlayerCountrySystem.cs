using System.Collections.Generic;
using ECS;
using GS.Game.Commands;
using GS.Game.Components;

namespace GS.Game.Systems {
	public static class SelectPlayerCountrySystem {
		public static void Update(World world, ReadCommands<SelectPlayerCountryCommand> commands) {
			if (commands.Count == 0) {
				return;
			}
			var span = commands.AsSpan();
			string targetId = span[span.Length - 1].CountryId;

			var toAdd = new List<int>();
			var toRemove = new List<int>();

			world.Query<Country>((int e, ref Country c) => {
				if (c.CountryId == targetId) {
					toAdd.Add(e);
				} else if (world.Has<Player>(e)) {
					toRemove.Add(e);
				}
			});

			foreach (int e in toRemove) {
				world.Remove<Player>(e);
			}
			foreach (int e in toAdd) {
				if (!world.Has<Player>(e)) {
					world.Add(e, new Player());
				}
			}
		}
	}
}
