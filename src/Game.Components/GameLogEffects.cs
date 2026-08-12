using System.Collections.Generic;
using GS.Game.Common;

namespace GS.Game.Components {
	public struct ControlEffectApplied {
		public string OrgId;
		public string CountryId;
		public double Delta;
		public double Total;
	}

	public struct OpinionEffectApplied {
		public string OrgId;
		public string CharacterId;
		public double Delta;
		public double Total; // raw, unclamped — VisualStateConverter applies the display clamp
	}

	public struct RoleChangeApplied {
		public string CountryId; // set for country-government roles, "" for org roles
		public string OrgId;     // set for org roles, "" for country-government roles
		public string RoleId;
		public string CharacterId;
	}

	public struct RelationSetApplied {
		public string OrgId;
		public string CountryId;
		public string TargetCountryId;
		public RelationKind Kind;
	}

	public struct WarDeclaredApplied {
		public string OrgId;
		public string CountryId;
		public string DefenderCountryId;
	}

	public struct WarResolvedApplied {
		public string WarId;
		public string AttackerCountryId;
		public string DefenderCountryId;
		public string WinnerCountryId;
		public string LoserCountryId;
		public double Progress;
		public double GoldTaken;
		public List<WarGoldRecipientSnapshot> GoldRecipients;
		public List<WarControlDeltaSnapshot> ControlDeltas;
		public List<WarProvinceTransferSnapshot> TransferredProvinces;
		public List<WarProgressHistorySnapshot> History;
		public WarSideStatsSnapshot Attacker;
		public WarSideStatsSnapshot Defender;
		public List<WarBattleRowSnapshot> Battles;
	}

	// Not [Savable] — one-shot destroy notification; created on destroy, read by
	// VisualStateConverter same tick, swept next tick by
	// CleanupEffectNotificationsSystem.UpdateCountryDestroyed (beside UpdateWarResolved).
	public struct CountryDestroyedApplied {
		public string CountryId;
	}
}
