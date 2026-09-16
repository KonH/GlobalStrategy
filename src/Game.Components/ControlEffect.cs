using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct ControlEffect {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		public int Value;
		[EffectId] public string EffectId;
	}
}
