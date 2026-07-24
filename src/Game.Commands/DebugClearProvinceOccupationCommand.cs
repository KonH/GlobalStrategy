namespace GS.Game.Commands {
	public struct DebugClearProvinceOccupationCommand : ICommand {
		[ProvinceId] public string ProvinceId;
	}
}
