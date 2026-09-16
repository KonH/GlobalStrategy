using System.Collections.Generic;
using GS.Game.Common;

namespace GS.Game.Components {
	public struct ControlEffectApplied {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		public double Delta;
		public double Total;
	}

	public struct OpinionEffectApplied {
		[OrgId] public string OrgId;
		[CharacterId] public string CharacterId;
		public double Delta;
		public double Total; // raw, unclamped — VisualStateConverter applies the display clamp
	}

	public struct RoleChangeApplied {
		[CountryId(AllowEmpty = true)] public string CountryId; // set for country-government roles, "" for org roles
		[OrgId(AllowEmpty = true)] public string OrgId;     // set for org roles, "" for country-government roles
		[RoleId] public string RoleId;
		[CharacterId] public string CharacterId;
	}

	public struct RelationSetApplied {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[CountryId] public string TargetCountryId;
		public RelationKind Kind;
	}

	public struct WarDeclaredApplied {
		[OrgId] public string OrgId;
		[CountryId] public string CountryId;
		[CountryId] public string DefenderCountryId;
	}

	public struct WarResolvedApplied {
		[WarId] public string WarId;
		[CountryId] public string AttackerCountryId;
		[CountryId] public string DefenderCountryId;
		[CountryId] public string WinnerCountryId;
		[CountryId] public string LoserCountryId;
		public double Progress;
		public double GoldTaken;
		[OmitFromSnapshot] public List<WarGoldRecipientSnapshot> GoldRecipients;
		[OmitFromSnapshot] public List<WarControlDeltaSnapshot> ControlDeltas;
		[OmitFromSnapshot] public List<WarProvinceTransferSnapshot> TransferredProvinces;
		[OmitFromSnapshot] public List<WarProgressHistorySnapshot> History;
		public WarSideStatsSnapshot Attacker;
		public WarSideStatsSnapshot Defender;
		[OmitFromSnapshot] public List<WarBattleRowSnapshot> Battles;
	}

	// Not [Savable] — one-shot destroy notification; created on destroy, read by
	// VisualStateConverter same tick, swept next tick by
	// CleanupEffectNotificationsSystem.UpdateCountryDestroyed (beside UpdateWarResolved).
	public struct CountryDestroyedApplied {
		[CountryId] public string CountryId;
	}

	// Not [Savable] — one-shot destroy notification; created on destroy, read by
	// VisualStateConverter in the same tick, then swept at the start of the next tick.
	public struct OrgDestroyedApplied {
		[OrgId] public string OrganizationId;
	}
}
