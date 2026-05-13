namespace GS.Game.Commands {
	public enum MapLens { Political, Geographic, Org }

	public struct ChangeLensCommand : ICommand {
		public MapLens Lens;
	}
}
