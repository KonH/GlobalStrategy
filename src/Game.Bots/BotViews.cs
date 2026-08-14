using System.Collections.Generic;

namespace GS.Game.Bots {
	public sealed class BotCardView {
		public string ActionId = "";
		public int SlotIndex;
		public string CountryId = "";
		public string TargetCountryId = "";
		public IReadOnlyList<BotCostView> Cost = System.Array.Empty<BotCostView>();
		public double GoldCost;
		public bool IsPlayable;
		public bool RaisesControl;
	}

	public sealed class BotCostView {
		public string ResourceId = "";
		public double Amount;
	}

	public sealed class BotCardDrawChoiceView {
		public int ChoiceIndex;
		public string ActionId = "";
		public string TargetCountryId = "";
		public IReadOnlyList<BotCostView> Cost = System.Array.Empty<BotCostView>();
		public double GoldCost;
		public bool RaisesControl;
		public bool IsPlayable;
		public bool IsControlUsable;
	}

	public sealed class BotOrgScoreView {
		public string OrgId = "";
		public double OrgScore;
	}

	public sealed class BotCountryView {
		public string CountryId = "";
		public int MyControl;
		public int TotalControl;
		public IReadOnlyList<BotControlShare> ControlByOrg = System.Array.Empty<BotControlShare>();
		public IReadOnlyList<BotCardView> Hand = System.Array.Empty<BotCardView>();
		public IReadOnlyList<BotCountryCharacterView> Characters = System.Array.Empty<BotCountryCharacterView>();
		public bool IsDestroyed;
		public bool IsAtWar;
		public string WarOpponentCountryId = "";
		public double OwnWarProgress;
		public IReadOnlyList<string> RivalCountryIds = System.Array.Empty<string>();
		public double CountryScore;
		public int OwnedProvinceCount;
		public int OccupiedOwnedProvinceCount;
		public double Recruits;
		public double Damage;
		public double Durability;
		public double TroopsDamageBonusPercent;
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
