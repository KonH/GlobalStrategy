namespace GS.Game.Components {
	[Savable]
	public record struct ResourceOwner(string OwnerId, OwnerType OwnerType = OwnerType.Org);
}
