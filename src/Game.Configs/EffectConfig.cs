using System.Collections.Generic;

namespace GS.Game.Configs {
	public class ActionEffectDefinition {
		public string EffectId    { get; set; } = "";
		public string EffectType  { get; set; } = "";
		public string NameKey     { get; set; } = "";
		public string DescKey     { get; set; } = "";
	}

	public class EffectConfig {
		public List<ActionEffectDefinition> Effects { get; set; } = new();

		public ActionEffectDefinition? Find(string effectId) {
			foreach (var e in Effects) {
				if (e.EffectId == effectId) { return e; }
			}
			return null;
		}
	}
}
