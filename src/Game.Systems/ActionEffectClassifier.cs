using GS.Game.Configs;

namespace GS.Game.Systems {
	public static class ActionEffectClassifier {
		public static bool RaisesControl(ActionDefinition? definition, EffectConfig effectConfig) {
			if (definition == null) {
				return false;
			}
			foreach (string effectId in definition.EffectIds) {
				if (effectConfig.Find(effectId) is ControlChangeEffectParams controlChange
					&& controlChange.Amount > 0) {
					return true;
				}
			}
			return false;
		}
	}
}
