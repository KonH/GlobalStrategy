namespace GS.Game.Bots {
	public interface IBotCommandSink {
		void DrawCountryCards();
		void ReceiveCountryCard(int choiceIndex);
		void PlayOrgCard(string actionId, int slotIndex);
		void PlayCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId);
		void DiscardCountryCard(string actionId, string countryId, int slotIndex, string targetCountryId);
	}
}
