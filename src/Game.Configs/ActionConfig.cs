using System.Collections.Generic;

namespace GS.Game.Configs {
	public class ActionCondition {
		public string ConditionType { get; set; } = "";
	}

	public class ActionPrice {
		public string ResourceId { get; set; } = "gold";
		public double Amount     { get; set; } = 0;
	}

	public class ActionDefinition {
		public string ActionId       { get; set; } = "";
		public string Rarity         { get; set; } = "Standard";
		public string NameKey        { get; set; } = "";
		public string DescKey        { get; set; } = "";
		public List<ActionCondition> Conditions { get; set; } = new();
		public List<ActionPrice> Prices { get; set; } = new();
		public List<string> EffectIds { get; set; } = new();
		public float SuccessRate     { get; set; } = 1.0f;
		public float MinCountryChance { get; set; } = 0.01f;
	}

	public class ActionOwnerDefaults {
		public string OwnerType { get; set; } = "";
		public int HandSize     { get; set; } = 0;
	}

	public class OrgActionPool {
		public string OrgId            { get; set; } = "";
		public List<string> ActionIds  { get; set; } = new();
	}

	public class ActionConfig {
		public List<ActionOwnerDefaults> Defaults  { get; set; } = new();
		public List<ActionDefinition>    Actions   { get; set; } = new();
		public List<OrgActionPool>       OrgPools  { get; set; } = new();

		public ActionDefinition? Find(string actionId) {
			foreach (var a in Actions) {
				if (a.ActionId == actionId) { return a; }
			}
			return null;
		}

		public int GetHandSize(string ownerType) {
			foreach (var d in Defaults) {
				if (d.OwnerType == ownerType) { return d.HandSize; }
			}
			return 0;
		}

		public List<string>? GetOrgPool(string orgId) {
			foreach (var p in OrgPools) {
				if (p.OrgId == orgId) { return p.ActionIds; }
			}
			return null;
		}
	}
}
