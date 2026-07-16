using System.Collections.Generic;

namespace GS.Game.Bots {
	public sealed class BotCardView {
		public string ActionId = "";
		public int SlotIndex;
		public string CountryId = "";
		public IReadOnlyList<BotCostView> Cost = System.Array.Empty<BotCostView>();
		public double GoldCost;
		public bool IsPlayable;
	}

	public sealed class BotCostView {
		public string ResourceId = "";
		public double Amount;
	}

	public sealed class BotCountryView {
		public string CountryId = "";
		public int MyControl;
		public int TotalControl;
		public IReadOnlyList<BotControlShare> ControlByOrg = System.Array.Empty<BotControlShare>();
		public IReadOnlyList<BotCardView> Hand = System.Array.Empty<BotCardView>();
		public IReadOnlyList<BotCountryCharacterView> Characters = System.Array.Empty<BotCountryCharacterView>();
	}

	public sealed class BotControlShare {
		public string OrgId = "";
		public int Control;
	}

	public sealed class BotCharacterSlotView {
		public string RoleId = "";
		public int SlotIndex;
		public bool IsAvailable;
		public string CharacterId = "";
	}

	public sealed class BotCountryCharacterView {
		public string CharacterId = "";
		public string RoleId = "";
		public double OpinionOfMyOrg;
	}
}
