namespace GS.Game.Commands {
	public struct DebugChangeProvinceOwnerCommand : ICommand {
		public string ProvinceId;
		public string NewOwnerId;
	}
}
