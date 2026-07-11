namespace GS.Game.Commands {
	public enum MapLens { Political, Geographic, Org, Province }

	public struct ChangeLensCommand : ICommand {
		public MapLens Lens;
	}
}
