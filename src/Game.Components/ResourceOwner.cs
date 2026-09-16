using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public record struct ResourceOwner([property: OwnerId] string OwnerId, OwnerType OwnerType = OwnerType.Org);
}
