using GS.Game.Common;

namespace GS.Game.Components {
	// Not [Savable] — transient UI-selection state, not part of persisted game state.
	public struct ProvinceSelection {
		[ProvinceId] public string ProvinceId;
	}
}
