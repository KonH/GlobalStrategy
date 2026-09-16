using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct BotActionLog {
		[OmitFromSnapshot] public string[] Entries;
	}
}
