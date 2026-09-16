using System;
using System.Collections.Generic;
using GS.Game.Common;

namespace GS.Game.Components {
	public struct WarProgressHistorySnapshot {
		[EffectId] public string EffectId;
		public double AppliedDelta;
		public DateTime Timestamp;
	}

	public struct WarEffectSnapshot {
		[EffectId] public string EffectId;
		public double Value;
		public PayType PayType;
		public double MaxTotal;
		public string OrgDisplayName;
	}

	public struct WarSideStatsSnapshot {
		[CountryId] public string CountryId;
		public double Recruits;
		public double TroopsInBattles;
		public double Casualties;
		public double Damage;
		public double Durability;
		public double DamageBase;
		public double DamageRulerBonus;
		public double DamageAdvisorBonus;
		public double DamageBonusPercent;
		[OmitFromSnapshot] public List<WarEffectSnapshot> DamageBonusEffects;
		public double DurabilityBase;
		public double DurabilityRulerBonus;
		public double DurabilityAdvisorBonus;
	}

	public struct WarBattleRowSnapshot {
		[BattleId] public string BattleId;
		[ProvinceId] public string ProvinceId;
		public bool IsFinished;
		[CountryId] public string WinnerCountryId;
		public WarParticipantKind WinnerSide;
		public double AttackerCasualties;
		public double DefenderCasualties;
		public double Progress;
		public double AttackerTroops;
		public double DefenderTroops;
	}

	public struct WarGoldRecipientSnapshot {
		public OwnerType OwnerType;
		[OwnerId] public string OwnerId;
		public double Amount;
	}

	public struct WarControlDeltaSnapshot {
		[CountryId] public string CountryId;
		[OrgId] public string OrgId;
		public int Delta;
		public int TotalAfter;
	}

	public struct WarProvinceTransferSnapshot {
		[ProvinceId] public string ProvinceId;
		[CountryId] public string OldOwnerCountryId;
		[CountryId] public string NewOwnerCountryId;
	}
}
