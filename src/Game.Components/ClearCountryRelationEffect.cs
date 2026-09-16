using GS.Game.Common;

namespace GS.Game.Components {
	// Not [Savable] — same-tick transient marker consumed by ClearCountryRelationSystem and
	// destroyed by CleanupActionEffectsSystem on the next tick, like other one-shot action effects.
	public struct ClearCountryRelationEffect {
		[EffectId] public string EffectId;
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[CountryId] public string TargetCountryId;
	}
}
