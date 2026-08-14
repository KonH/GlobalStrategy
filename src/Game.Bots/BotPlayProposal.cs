namespace GS.Game.Bots {
	public sealed class BotPlayProposal {
		public string FeatureId = "";
		public string ActionId = "";
		public string CountryId = "";
		public string TargetCountryId = "";
		public int SlotIndex;
		public double EstimatedDeltaOrgScore;
		public bool ApplyRevengeSoftWeight;
		public double TargetBiasMultiplier = 1.0;
	}
}
