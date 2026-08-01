using System;
using System.Collections.Generic;
using GS.Game.Common;

namespace GS.Game.Components {
	public struct WarProgressHistorySnapshot {
		public string EffectId;
		public double AppliedDelta;
		public DateTime Timestamp;
	}

	public struct WarEffectSnapshot {
		public string EffectId;
		public double Value;
		public PayType PayType;
		public double MaxTotal;
		public string OrgDisplayName;
	}

	public struct WarSideStatsSnapshot {
		public string CountryId;
		public double Recruits;
		public double TroopsInBattles;
		public double Casualties;
		public double Damage;
		public double Durability;
		public double DamageBase;
		public double DamageRulerBonus;
		public double DamageAdvisorBonus;
		public double DamageBonusPercent;
		public List<WarEffectSnapshot> DamageBonusEffects;
		public double DurabilityBase;
		public double DurabilityRulerBonus;
		public double DurabilityAdvisorBonus;
	}

	public struct WarBattleRowSnapshot {
		public string BattleId;
		public string ProvinceId;
		public bool IsFinished;
		public string WinnerCountryId;
		public WarParticipantKind WinnerSide;
		public double AttackerCasualties;
		public double DefenderCasualties;
		public double Progress;
		public double AttackerTroops;
		public double DefenderTroops;
	}

	public struct WarGoldRecipientSnapshot {
		public OwnerType OwnerType;
		public string OwnerId;
		public double Amount;
	}

	public struct WarControlDeltaSnapshot {
		public string CountryId;
		public string OrgId;
		public int Delta;
		public int TotalAfter;
	}

	public struct WarProvinceTransferSnapshot {
		public string ProvinceId;
		public string OldOwnerCountryId;
		public string NewOwnerCountryId;
	}
}
