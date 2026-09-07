using System.Collections.Generic;

namespace GS.Game.E2E {
	public class StepScript {
		public string Name { get; set; } = "";
		public List<StepDefinition> Steps { get; set; } = new List<StepDefinition>();
	}

	public class StepDefinition {
		public string Kind { get; set; } = "";
		public string? Name { get; set; }
		public string? Label { get; set; }
		public string? Org { get; set; }
		public string? Country { get; set; }
		public string? Save { get; set; }
		public StepRowSelector? Row { get; set; }
		public string? Value { get; set; }
		public string? Line { get; set; }
		public string? Screen { get; set; }
		public string? Control { get; set; }
		public string? GameDate { get; set; }
		public bool? Pause { get; set; }
		public int? Frames { get; set; }
		public int? TimeoutSeconds { get; set; }
		public List<string>? ExtraState { get; set; }
	}

	public class StepRowSelector {
		public int Index { get; set; }
	}
}
