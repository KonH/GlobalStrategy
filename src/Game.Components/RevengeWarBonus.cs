using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct RevengeWarBonus {
		public string WarId;
		public string CountryId;
		public double DamageBonusPercent;
		public double DurabilityBonusPercent;
	}
}
