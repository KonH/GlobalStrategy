using System.Collections.Generic;

namespace GS.Game.E2E {
	public class CoreStateSnapshot {
		public string Scene { get; set; } = "";
		public string Screen { get; set; } = "";
		public string ActiveOrg { get; set; } = "";
		public string GameDate { get; set; } = "";
		public bool IsPaused { get; set; }
		public string SelectedCountry { get; set; } = "";
		public string SelectedProvince { get; set; } = "";
		public string InputPath { get; set; } = "";
		public Dictionary<string, string> Extra { get; set; } = new Dictionary<string, string>();
	}
}
