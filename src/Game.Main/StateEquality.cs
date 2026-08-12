using System;
using System.Collections.Generic;
using GS.Game.Configs;

namespace GS.Main {
	public static class StateEquality {
		public static bool ListEquals<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, Func<T, T, bool> elementEquals) {
			if (ReferenceEquals(a, b)) {
				return true;
			}
			if (a.Count != b.Count) {
				return false;
			}
			for (int i = 0; i < a.Count; i++) {
				if (!elementEquals(a[i], b[i])) {
					return false;
				}
			}
			return true;
		}

		public static bool DictionaryContentEquals<TValue>(IReadOnlyDictionary<string, TValue> a, IReadOnlyDictionary<string, TValue> b) {
			if (ReferenceEquals(a, b)) {
				return true;
			}
			if (a.Count != b.Count) {
				return false;
			}
			var comparer = EqualityComparer<TValue>.Default;
			foreach (var kvp in a) {
				if (!b.TryGetValue(kvp.Key, out var otherValue) || !comparer.Equals(kvp.Value, otherValue)) {
					return false;
				}
			}
			return true;
		}

		public static bool OrgControlEntryEquals(OrgControlEntry a, OrgControlEntry b) {
			return a.OrgId == b.OrgId
				&& a.DisplayName == b.DisplayName
				&& a.Control == b.Control
				&& a.BaseControl == b.BaseControl
				&& a.PermanentControl == b.PermanentControl
				&& a.EstimatedMonthlyGold == b.EstimatedMonthlyGold;
		}

		public static bool SkillEntryEquals(SkillEntry a, SkillEntry b) {
			return a.SkillId == b.SkillId && a.Value == b.Value;
		}

		public static bool CharacterStateEntryEquals(CharacterStateEntry? a, CharacterStateEntry? b) {
			if (a == null || b == null) {
				return a == null && b == null;
			}
			return a.CharacterId == b.CharacterId
				&& a.RoleId == b.RoleId
				&& NamePartKeysEquals(a.NamePartKeys, b.NamePartKeys)
				&& ListEquals(a.Skills, b.Skills, SkillEntryEquals)
				&& a.Opinion.Actual == b.Opinion.Actual;
		}

		static bool NamePartKeysEquals(string[] a, string[] b) {
			if (ReferenceEquals(a, b)) {
				return true;
			}
			if (a.Length != b.Length) {
				return false;
			}
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != b[i]) {
					return false;
				}
			}
			return true;
		}

		public static bool OrgCharacterSlotEntryEquals(OrgCharacterSlotEntry a, OrgCharacterSlotEntry b) {
			return a.RoleId == b.RoleId
				&& a.SlotIndex == b.SlotIndex
				&& a.IsAvailable == b.IsAvailable
				&& CharacterStateEntryEquals(a.Character, b.Character);
		}

		public static bool OrgCountryEntryEquals(OrgCountryEntry a, OrgCountryEntry b) {
			return a.CountryId == b.CountryId
				&& a.TopOrgId == b.TopOrgId
				&& a.ControlRatio == b.ControlRatio;
		}

		public static bool ActionCardEntryEquals(ActionCardEntry a, ActionCardEntry b) {
			return a.ActionId == b.ActionId
				&& a.SlotIndex == b.SlotIndex
				&& a.IsInHand == b.IsInHand
				&& a.CanPlay == b.CanPlay
				&& a.IsUnplayable == b.IsUnplayable
				&& a.UnplayableReason == b.UnplayableReason
				&& ActionConditionDebugEntryNullableEquals(a.FirstFailure, b.FirstFailure)
				&& a.CountryContextId == b.CountryContextId
				&& a.TargetCountryId == b.TargetCountryId
				&& ListEquals(a.PlayableCountryIds, b.PlayableCountryIds, (x, y) => x == y)
				&& a.WarWinChancePercent == b.WarWinChancePercent
				&& a.CooldownRemainingDays == b.CooldownRemainingDays
				&& a.CooldownFractionRemaining == b.CooldownFractionRemaining
				&& ListEquals(a.Conditions, b.Conditions, ActionConditionDebugEntryEquals);
		}

		public static bool CardDrawChoiceEntryEquals(CardDrawChoiceEntry a, CardDrawChoiceEntry b) {
			return a.ChoiceIndex == b.ChoiceIndex
				&& ActionCardEntryEquals(a.Card, b.Card);
		}

		public static bool ActionConditionDebugEntryEquals(ActionConditionDebugEntry a, ActionConditionDebugEntry b) {
			return a.Label == b.Label
				&& a.Passed == b.Passed
				&& a.LocaleKey == b.LocaleKey
				&& a.ReasonCode == b.ReasonCode
				&& ListEquals(a.LocaleArguments, b.LocaleArguments, (x, y) => x == y);
		}

		static bool ActionConditionDebugEntryNullableEquals(ActionConditionDebugEntry? a, ActionConditionDebugEntry? b) {
			if (a == null || b == null) { return a == null && b == null; }
			return ActionConditionDebugEntryEquals(a, b);
		}

		public static bool VisualResourceChangeEffectEquals(VisualResourceChangeEffect a, VisualResourceChangeEffect b) {
			return a.EffectId == b.EffectId
				&& a.ResourceId == b.ResourceId
				&& a.OwnerId == b.OwnerId
				&& a.Amount == b.Amount;
		}

		public static bool LeaderboardEntryStateEquals(LeaderboardEntryState a, LeaderboardEntryState b) {
			return a.Place == b.Place
				&& a.EntityId == b.EntityId
				&& a.DisplayName == b.DisplayName
				&& a.Score == b.Score;
		}

		public static bool GoalProgressEntryStateEquals(GoalProgressEntryState a, GoalProgressEntryState b) {
			return a.Kind == b.Kind
				&& a.ConfigValue == b.ConfigValue
				&& a.Current == b.Current
				&& a.Target == b.Target
				&& a.AvailableCountryCount == b.AvailableCountryCount;
		}

		public static bool GoalsOrgEntryStateEquals(GoalsOrgEntryState a, GoalsOrgEntryState b) {
			return a.OrgId == b.OrgId
				&& ListEquals(a.Goals, b.Goals, GoalProgressEntryStateEquals);
		}

		public static bool ActiveTaskRewardStateEquals(ActiveTaskRewardState a, ActiveTaskRewardState b) {
			return a.ResourceId == b.ResourceId && a.Amount == b.Amount;
		}

		public static bool ActiveTaskEntryStateEquals(ActiveTaskEntryState a, ActiveTaskEntryState b) {
			return a.TaskId == b.TaskId
				&& a.NameKey == b.NameKey
				&& a.DescKey == b.DescKey
				&& ListEquals(a.Rewards, b.Rewards, ActiveTaskRewardStateEquals);
		}

		public static bool WarIconEntryStateEquals(WarIconEntryState a, WarIconEntryState b) {
			return a.WarId == b.WarId
				&& a.Progress == b.Progress
				&& a.AttackerCountryId == b.AttackerCountryId
				&& a.DefenderCountryId == b.DefenderCountryId;
		}

		public static bool WarProgressHistoryEntryStateEquals(WarProgressHistoryEntryState a, WarProgressHistoryEntryState b) {
			return a.EffectId == b.EffectId
				&& a.AppliedDelta == b.AppliedDelta
				&& a.Timestamp == b.Timestamp;
		}

		public static bool WarSideStatsStateEquals(WarSideStatsState a, WarSideStatsState b) {
			return a.CountryId == b.CountryId
				&& a.RecruitsAvailable == b.RecruitsAvailable
				&& a.TroopsInBattles == b.TroopsInBattles
				&& a.Casualties == b.Casualties
				&& a.Damage == b.Damage
				&& a.Durability == b.Durability
				&& a.DamageBase == b.DamageBase
				&& a.DamageRulerBonus == b.DamageRulerBonus
				&& a.DamageAdvisorBonus == b.DamageAdvisorBonus
				&& a.DamageBonusPercent == b.DamageBonusPercent
				&& ListEquals(a.DamageBonusEffects, b.DamageBonusEffects, EffectStateEntryEquals)
				&& a.DurabilityBase == b.DurabilityBase
				&& a.DurabilityRulerBonus == b.DurabilityRulerBonus
				&& a.DurabilityAdvisorBonus == b.DurabilityAdvisorBonus;
		}

		public static bool WarBattleRowStateEquals(WarBattleRowState a, WarBattleRowState b) {
			return a.BattleId == b.BattleId
				&& a.ProvinceId == b.ProvinceId
				&& a.IsFinished == b.IsFinished
				&& a.WinnerCountryId == b.WinnerCountryId
				&& a.WinnerSide == b.WinnerSide
				&& a.AttackerCasualties == b.AttackerCasualties
				&& a.DefenderCasualties == b.DefenderCasualties
				&& a.Progress == b.Progress
				&& a.AttackerTroops == b.AttackerTroops
				&& a.DefenderTroops == b.DefenderTroops;
		}

		public static bool GameLogEntryEquals(GameLogEntry a, GameLogEntry b) {
			return a.SequenceId == b.SequenceId
				&& a.Kind == b.Kind
				&& a.OrgId == b.OrgId
				&& a.CountryId == b.CountryId
				&& a.CharacterId == b.CharacterId
				&& a.RoleId == b.RoleId
				&& NamePartKeysEquals(a.NamePartKeys, b.NamePartKeys)
				&& a.Delta == b.Delta
				&& a.Total == b.Total
				&& a.IsOrgRole == b.IsOrgRole
				&& a.TargetCountryId == b.TargetCountryId
				&& a.RelationKind == b.RelationKind;
		}

		public static bool EffectStateEntryEquals(EffectStateEntry a, EffectStateEntry b) {
			return a.EffectId == b.EffectId
				&& a.Value == b.Value
				&& a.PayType == b.PayType
				&& a.MaxTotal == b.MaxTotal
				&& a.OrgDisplayName == b.OrgDisplayName
				&& BaseIncomeBreakdownEquals(a.BaseIncomeBreakdown, b.BaseIncomeBreakdown);
		}

		static bool BaseIncomeBreakdownEquals(BaseIncomeBreakdownState? a, BaseIncomeBreakdownState? b) {
			if (a == null && b == null) {
				return true;
			}
			if (a == null || b == null) {
				return false;
			}
			return a.FlatBase == b.FlatBase
				&& a.Population == b.Population
				&& a.PopulationContribution == b.PopulationContribution
				&& a.ProvinceCount == b.ProvinceCount
				&& a.ProvinceContribution == b.ProvinceContribution
				&& a.AdvisorSkill == b.AdvisorSkill
				&& a.AdvisorContribution == b.AdvisorContribution;
		}

		public static bool ResourceStateEntryEquals(ResourceStateEntry a, ResourceStateEntry b) {
			return a.ResourceId == b.ResourceId
				&& a.Value.Actual == b.Value.Actual
				&& ListEquals(a.Effects, b.Effects, EffectStateEntryEquals);
		}

		public static bool ControlIncomeEntryEquals(ControlIncomeEntry a, ControlIncomeEntry b) {
			return a.CountryId == b.CountryId && a.MonthlyGold == b.MonthlyGold;
		}
	}
}
