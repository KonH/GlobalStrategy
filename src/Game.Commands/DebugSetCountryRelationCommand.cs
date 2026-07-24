using GS.Game.Common;

namespace GS.Game.Commands {
	public struct DebugSetCountryRelationCommand : ICommand {
		public string CountryIdA;
		public string CountryIdB;
		public RelationKind Kind;
	}
}
