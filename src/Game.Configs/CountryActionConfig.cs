using System.Collections.Generic;

namespace GS.Game.Configs {
	public class CountryActionDefinition {
		public string ActionId { get; set; } = "";
		public string NameKey { get; set; } = "";
		public string DescKey { get; set; } = "";
		public string TargetRole { get; set; } = "";
		public int DeckCopies { get; set; } = 3;
		public bool PreDealtToHand { get; set; }
		public int CooldownMonths { get; set; }
		public int InfluenceThreshold { get; set; }
		public float SuccessRateBase { get; set; }
		public int SuccessRateInfluenceDivisor { get; set; }
		public double GoldCost { get; set; }
		public int InfluenceOnSuccess { get; set; }
		public string OpinionModifierSourceId { get; set; } = "";
		public int OpinionModifierValue { get; set; }
		public int OpinionModifierChangeValue { get; set; }
	}

	public class CountryActionConfig {
		public List<CountryActionDefinition> Actions { get; set; } = new();

		public CountryActionDefinition? Find(string actionId) {
			foreach (var def in Actions) {
				if (def.ActionId == actionId) { return def; }
			}
			return null;
		}
	}
}
