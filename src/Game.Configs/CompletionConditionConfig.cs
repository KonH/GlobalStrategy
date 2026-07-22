using System.Collections.Generic;
using Newtonsoft.Json;

namespace GS.Game.Configs {
	public class CompletionConditionConfig {
		public string Type { get; set; } = "";
		public double Value { get; set; }
		[JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
		public List<CompletionConditionConfig> Members { get; set; } = new List<CompletionConditionConfig>();
	}
}
