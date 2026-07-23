using System;
using System.Collections.Generic;

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
				&& a.IsUnplayable == b.IsUnplayable
				&& a.UnplayableReason == b.UnplayableReason;
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
				&& a.IsOrgRole == b.IsOrgRole;
		}

		public static bool EffectStateEntryEquals(EffectStateEntry a, EffectStateEntry b) {
			return a.EffectId == b.EffectId
				&& a.Value == b.Value
				&& a.PayType == b.PayType;
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
