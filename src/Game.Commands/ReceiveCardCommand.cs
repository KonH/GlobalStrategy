namespace GS.Game.Commands {
	public struct ReceiveCardCommand : ICommand {
		[OrgId] public string OrgId;
		public int ChoiceIndex;
	}
}
