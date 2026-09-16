using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct RevengeWarBonus {
		[WarId] public string WarId;
		[CountryId] public string CountryId;
		public double DamageBonusPercent;
		public double DurabilityBonusPercent;
	}
}
