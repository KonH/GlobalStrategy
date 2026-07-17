using System.Collections.Generic;

namespace GS.Game.Bots {
	public class BotProfile {
		public string OrgId { get; set; } = "";
		public List<BotFeatureSetting> Features { get; set; } = new();
	}

	public class BotFeatureSetting {
		public string FeatureId { get; set; } = "";
		public bool Enabled { get; set; }
		public Dictionary<string, double> Parameters { get; set; } = new();
	}
}
