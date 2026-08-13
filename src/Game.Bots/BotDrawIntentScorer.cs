using System;
using System.Collections.Generic;

namespace GS.Game.Bots {
	public static class BotDrawIntentScorer {
		static readonly HashSet<string> WarOrUnlockActionIds = new HashSet<string>(StringComparer.Ordinal) {
			"make_rival",
			"improve_diplomacy_advisor_opinion",
			"improve_military_advisor_opinion",
			"improve_ruler_opinion",
			"declare_war",
			"declare_revenge_war",
			"sell_arms"
		};

		public static bool IsKnownWarOrUnlockAction(string actionId) {
			return WarOrUnlockActionIds.Contains(actionId);
		}

		public static double ScoreChoice(BotCardDrawChoiceView choice, IBotObservation obs) {
			_ = obs;
			if (choice.IsControlUsable) {
				return 400;
			}
			if (choice.RaisesControl) {
				return 300;
			}
			if (IsKnownWarOrUnlockAction(choice.ActionId)) {
				return 200;
			}
			if (choice.IsPlayable) {
				return 100;
			}
			return 0;
		}
	}
}
