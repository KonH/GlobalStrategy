using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct ProvinceOwnership {
		[ProvinceId] public string ProvinceId;
		[CountryId] public string OwnerId;
	}
}
