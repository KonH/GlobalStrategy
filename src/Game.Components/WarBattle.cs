using GS.Game.Common;

namespace GS.Game.Components {
	[Savable]
	public struct WarBattleCapacity {
		public int MaxConcurrentBattleCount;
	}

	[Savable]
	public struct Battle {
		public string BattleId;
		public string WarId;
		public string TargetProvinceId;
		public BattleState State;
		public WarParticipantKind Winner;
	}

	[Savable]
	public struct BattleForce {
		public string BattleId;
		public string CountryId;
		public WarParticipantKind Side;
		public double Troops;
		public double Casualties;
	}
}
