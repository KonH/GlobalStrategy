using System;
using System.Collections.Generic;
using ECS;
using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class EventNotificationConditionContext {
		public static bool Passes(
			IReadOnlyWorld world,
			string playerOrgId,
			IReadOnlyList<string> participantCountryIds,
			ExpressionNode? condition,
			Func<string, int>? controlForCountry = null) {
			if (condition == null) {
				return true;
			}

			if (string.IsNullOrEmpty(playerOrgId)) {
				return ExpressionNode.Evaluate(condition, new ExpressionContext { Control = 0 }) != 0;
			}

			for (int i = 0; i < participantCountryIds.Count; i++) {
				string countryId = participantCountryIds[i];
				int control = controlForCountry != null
					? controlForCountry(countryId)
					: ControlQuery.GetOrgControlInCountry(world, playerOrgId, countryId);
				var ctx = new ExpressionContext { Control = control };
				if (ExpressionNode.Evaluate(condition, ctx) != 0) {
					return true;
				}
			}

			return false;
		}
	}
}
