using System.Collections.Generic;

namespace GS.Game.Configs {
	public class ResourceConfig {
		public List<ResourceDefinition> Resources { get; set; } = new List<ResourceDefinition>();

		public ResourceDefinition? FindResource(string resourceId) {
			foreach (var r in Resources) {
				if (r.ResourceId == resourceId) return r;
			}
			return null;
		}
	}

	public class ResourceDefinition {
		public string ResourceId { get; set; } = "";
		public string NameKey { get; set; } = "";
		public string DescriptionKey { get; set; } = "";
		public string Icon { get; set; } = "";
		public double DefaultInitialValue { get; set; } = 100.0;
		public List<EffectDefinition> DefaultEffects { get; set; } = new List<EffectDefinition>();

		public EffectDefinition? FindEffect(string effectId) {
			foreach (var e in DefaultEffects) {
				if (e.EffectId == effectId) return e;
			}
			return null;
		}
	}

	public class EffectDefinition {
		public string EffectId { get; set; } = "";
		public string NameKey { get; set; } = "";
		public string DescriptionKey { get; set; } = "";
		public double Value { get; set; } = 0;
		public string PayType { get; set; } = "Monthly";
	}
}
