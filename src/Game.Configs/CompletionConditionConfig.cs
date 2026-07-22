using System.Collections.Generic;

namespace GS.Game.Configs {
	public class CompletionConditionConfig {
		public string Type { get; set; } = "";
		public double Value { get; set; }
		public List<CompletionConditionConfig> Members { get; set; } = new List<CompletionConditionConfig>();
	}
}
