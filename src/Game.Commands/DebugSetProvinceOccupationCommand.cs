namespace GS.Game.Commands {
	public struct DebugSetProvinceOccupationCommand : ICommand {
		[ProvinceId] public string ProvinceId;
		[CountryId] public string OccupierId;
	}
}
