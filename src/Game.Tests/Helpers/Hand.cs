using System.Collections.Generic;
using GS.Main;
using Xunit;

namespace GS.Game.Tests.Helpers {
	/// <summary>Lookups into a projected hand of <see cref="ActionCardEntry"/> items.</summary>
	public static class Hand {
		/// <summary>The entry for <paramref name="actionId"/>, or <c>null</c> when the hand has none.</summary>
		public static ActionCardEntry? Find(IReadOnlyList<ActionCardEntry> entries, string actionId) {
			foreach (ActionCardEntry entry in entries) {
				if (entry.ActionId == actionId) {
					return entry;
				}
			}
			return null;
		}

		/// <summary>The entry for <paramref name="actionId"/>, failing the test when it is absent.</summary>
		public static ActionCardEntry Require(IReadOnlyList<ActionCardEntry> entries, string actionId) {
			ActionCardEntry? entry = Find(entries, actionId);
			Assert.NotNull(entry);
			return entry!;
		}

		public static IReadOnlyList<string> ActionIds(IReadOnlyList<ActionCardEntry> entries) {
			var result = new List<string>(entries.Count);
			foreach (ActionCardEntry entry in entries) {
				result.Add(entry.ActionId);
			}
			return result;
		}
	}
}
