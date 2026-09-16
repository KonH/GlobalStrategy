using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public record struct Country([property: CountryId] string CountryId);
}
