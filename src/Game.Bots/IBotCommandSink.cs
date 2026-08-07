namespace GS.Game.Bots {
	public interface IBotCommandSink {
		void PlayOrgCard(string actionId, int slotIndex);
		void PlayCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId);
	}
}
