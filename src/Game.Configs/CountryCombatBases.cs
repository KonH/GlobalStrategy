namespace GS.Game.Configs {
	public sealed class CountryCombatBases {
		public int BaseDamage { get; }
		public int BaseDurability { get; }

		public CountryCombatBases(int baseDamage, int baseDurability) {
			BaseDamage = baseDamage;
			BaseDurability = baseDurability;
		}
	}
}
