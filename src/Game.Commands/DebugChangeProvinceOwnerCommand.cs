namespace GS.Game.Commands {
	public struct DebugChangeProvinceOwnerCommand : ICommand {
		[ProvinceId] public string ProvinceId;
		[CountryId] public string NewOwnerId;
	}
}
