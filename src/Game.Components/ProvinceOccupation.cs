using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct ProvinceOccupation {
		[ProvinceId] public string ProvinceId;
		[CountryId] public string OccupierId;
	}
}
