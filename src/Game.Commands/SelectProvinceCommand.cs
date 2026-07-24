namespace GS.Game.Commands {
	public struct SelectProvinceCommand : ICommand {
		[ProvinceId] public string ProvinceId;
	}
}
