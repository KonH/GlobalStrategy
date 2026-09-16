using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public record struct ResourceLink([property: ResourceId] string ResourceId);
}
