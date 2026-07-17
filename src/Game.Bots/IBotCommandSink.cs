namespace GS.Game.Bots {
	public interface IBotCommandSink {
		void PlayOrgCard(string actionId);
		void PlayCountryCard(string actionId, string countryId);
	}
}
