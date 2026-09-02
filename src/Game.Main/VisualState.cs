using System;
using System.Collections.Generic;
using System.ComponentModel;
using GS.Game.Common;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Main {
	public class SelectedCountryState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string CountryId { get; private set; } = "";
		public CountryResourcesState Resources { get; } = new CountryResourcesState();
		public CountryControlState Control { get; } = new CountryControlState();
		public CountryCharactersState Characters { get; } = new CountryCharactersState();
		public CountryActionsState CountryActions { get; } = new CountryActionsState();
		public CountryRelationsState Relations { get; } = new CountryRelationsState();
		public CountryWarsState Wars { get; } = new CountryWarsState();

		public void Set(bool isValid, string countryId) {
			if (IsValid == isValid && CountryId == countryId) {
				return;
			}
			IsValid = isValid;
			CountryId = countryId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class CountryRelationsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyList<string> Friends { get; private set; } = Array.Empty<string>();
		public IReadOnlyList<string> Rivals { get; private set; } = Array.Empty<string>();

		public void Set(IReadOnlyList<string> friends, IReadOnlyList<string> rivals) {
			var equal = new HashSet<string>(Friends).SetEquals(friends) && new HashSet<string>(Rivals).SetEquals(rivals);
			if (equal) {
				return;
			}
			Friends = friends;
			Rivals = rivals;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class CountryWarsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyList<string> Opponents { get; private set; } = Array.Empty<string>();

		public void Set(IReadOnlyList<string> opponents) {
			if (new HashSet<string>(Opponents).SetEquals(opponents)) {
				return;
			}
			Opponents = opponents;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class LocaleState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public string Locale { get; private set; } = "";

		public void Set(string locale) {
			if (Locale == locale) {
				return;
			}
			Locale = locale;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class PlayerOrganizationState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string OrgId { get; private set; } = "";
		public string DisplayName { get; private set; } = "";
		public string HqCountryId { get; private set; } = "";
		public bool IsDestroyed { get; private set; }
		public CountryResourcesState Resources { get; } = new CountryResourcesState();
		public OrgCharactersState Characters { get; } = new OrgCharactersState();
		public OrgActionsState Actions { get; } = new OrgActionsState();

		public void Set(bool isValid, string orgId, string displayName, string hqCountryId = "", bool isDestroyed = false) {
			if (IsValid == isValid && OrgId == orgId && DisplayName == displayName
				&& HqCountryId == hqCountryId && IsDestroyed == isDestroyed) {
				return;
			}
			IsValid = isValid;
			OrgId = orgId;
			DisplayName = displayName;
			HqCountryId = hqCountryId;
			IsDestroyed = isDestroyed;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class SelectedOrganizationState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string OrgId { get; private set; } = "";
		public string DisplayName { get; private set; } = "";
		public double InitialGold { get; private set; }

		public void Set(bool isValid, string orgId, string displayName, double initialGold) {
			if (IsValid == isValid && OrgId == orgId && DisplayName == displayName && InitialGold == initialGold) {
				return;
			}
			IsValid = isValid;
			OrgId = orgId;
			DisplayName = displayName;
			InitialGold = initialGold;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class OrgControlEntry {
		public string OrgId { get; }
		public string DisplayName { get; }
		public int Control { get; }
		public int BaseControl { get; }
		public int PermanentControl { get; }
		public double EstimatedMonthlyGold { get; }

		public OrgControlEntry(string orgId, string displayName, int control, int baseControl, int permanentControl, double estimatedMonthlyGold) {
			OrgId = orgId;
			DisplayName = displayName;
			Control = control;
			BaseControl = baseControl;
			PermanentControl = permanentControl;
			EstimatedMonthlyGold = estimatedMonthlyGold;
		}
	}

	public class CountryControlState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public AnimatableInt UsedControl { get; } = new AnimatableInt();
		public int PoolSize { get; set; } = 100;
		public IReadOnlyList<OrgControlEntry> OrgEntries { get; private set; } = Array.Empty<OrgControlEntry>();

		public void Set(int used, List<OrgControlEntry> entries) {
			bool usedChanged = UsedControl.Actual != used;
			UsedControl.SetActual(used);
			if (!usedChanged && StateEquality.ListEquals(OrgEntries, entries, StateEquality.OrgControlEntryEquals)) {
				return;
			}
			OrgEntries = entries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class SkillEntry {
		public string SkillId { get; }
		public int Value { get; }
		public SkillEntry(string skillId, int value) { SkillId = skillId; Value = value; }
	}

	public class CharacterStateEntry {
		public string CharacterId { get; }
		public string RoleId { get; }
		public string[] NamePartKeys { get; }
		public IReadOnlyList<SkillEntry> Skills { get; }
		public AnimatableInt Opinion { get; }

		// Frozen at construction time — see ResourceStateEntry.ActualSnapshot for why comparing
		// Opinion.Actual directly (a cached AnimatableInt reused across ticks) can't detect change.
		public int OpinionSnapshot { get; }

		public CharacterStateEntry(string characterId, string roleId, string[] namePartKeys, IReadOnlyList<SkillEntry> skills, AnimatableInt opinion) {
			CharacterId = characterId;
			RoleId = roleId;
			NamePartKeys = namePartKeys;
			Skills = skills;
			Opinion = opinion;
			OpinionSnapshot = opinion.Actual;
		}
	}

	public class CountryCharactersState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<CharacterStateEntry> Characters { get; private set; } = Array.Empty<CharacterStateEntry>();
		public void Set(List<CharacterStateEntry> characters) {
			if (StateEquality.ListEquals(Characters, characters, StateEquality.CharacterStateEntryEquals)) {
				return;
			}
			Characters = characters;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class OrgCharacterSlotEntry {
		public string RoleId { get; }
		public int SlotIndex { get; }
		public CharacterStateEntry? Character { get; }
		public bool IsAvailable { get; }
		public OrgCharacterSlotEntry(string roleId, int slotIndex, CharacterStateEntry? character, bool isAvailable) {
			RoleId = roleId;
			SlotIndex = slotIndex;
			Character = character;
			IsAvailable = isAvailable;
		}
	}

	public class OrgCharactersState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<OrgCharacterSlotEntry> Slots { get; private set; } = Array.Empty<OrgCharacterSlotEntry>();
		public void Set(List<OrgCharacterSlotEntry> slots) {
			if (StateEquality.ListEquals(Slots, slots, StateEquality.OrgCharacterSlotEntryEquals)) {
				return;
			}
			Slots = slots;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class MapLensState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public MapLens Lens { get; private set; } = MapLens.Political;
		public void Set(MapLens lens) {
			if (Lens == lens) {
				return;
			}
			Lens = lens;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class OrgCountryEntry {
		public string CountryId { get; }
		public string TopOrgId { get; }
		public float ControlRatio { get; }
		public OrgCountryEntry(string countryId, string topOrgId, float controlRatio) {
			CountryId = countryId;
			TopOrgId = topOrgId;
			ControlRatio = controlRatio;
		}
	}

	public class OrgMapState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<OrgCountryEntry> Entries { get; private set; } = Array.Empty<OrgCountryEntry>();
		public void Set(List<OrgCountryEntry> entries) {
			if (StateEquality.ListEquals(Entries, entries, StateEquality.OrgCountryEntryEquals)) {
				return;
			}
			Entries = entries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class WorldCountriesState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public HashSet<string> CountryIds { get; private set; } = new HashSet<string>();
		public HashSet<string> DestroyedCountryIds { get; private set; } = new HashSet<string>();

		public void Set(HashSet<string> ids, HashSet<string>? destroyedIds = null) {
			destroyedIds ??= new HashSet<string>();
			if (CountryIds.SetEquals(ids) && DestroyedCountryIds.SetEquals(destroyedIds)) {
				return;
			}
			CountryIds = ids;
			DestroyedCountryIds = destroyedIds;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class ActionCardEntry {
		public string ActionId        { get; }
		public int    SlotIndex       { get; }
		public bool   IsInHand        { get; }
		public bool   CanPlay         { get; }
		public bool   IsUnplayable    { get; }
		public string UnplayableReason { get; }
		public ActionConditionDebugEntry? FirstFailure { get; }
		public string CountryContextId { get; }
		public string TargetCountryId { get; }
		public IReadOnlyList<string> PlayableCountryIds { get; }
		public int?   WarWinChancePercent { get; }
		public double? CooldownRemainingDays { get; }
		public double? CooldownFractionRemaining { get; }
		public IReadOnlyList<ActionConditionDebugEntry> Conditions { get; }
		public ActionCardEntry(
			string actionId,
			int slotIndex,
			bool isInHand,
			bool isUnplayable = false,
			string unplayableReason = "",
			string targetCountryId = "",
			IReadOnlyList<ActionConditionDebugEntry>? conditions = null,
			int? warWinChancePercent = null,
			double? cooldownRemainingDays = null,
			double? cooldownFractionRemaining = null,
			bool? canPlay = null,
			ActionConditionDebugEntry? firstFailure = null,
			IReadOnlyList<string>? playableCountryIds = null,
			string countryContextId = "") {
			ActionId = actionId; SlotIndex = slotIndex; IsInHand = isInHand;
			CanPlay = canPlay ?? !isUnplayable;
			IsUnplayable = !CanPlay; UnplayableReason = unplayableReason;
			FirstFailure = firstFailure;
			CountryContextId = countryContextId;
			TargetCountryId = targetCountryId;
			PlayableCountryIds = playableCountryIds ?? Array.Empty<string>();
			WarWinChancePercent = warWinChancePercent;
			CooldownRemainingDays = cooldownRemainingDays;
			CooldownFractionRemaining = cooldownFractionRemaining;
			Conditions = conditions ?? Array.Empty<ActionConditionDebugEntry>();
		}
	}

	public class CardDrawChoiceEntry {
		public int ChoiceIndex { get; }
		public ActionCardEntry Card { get; }

		public CardDrawChoiceEntry(int choiceIndex, ActionCardEntry card) {
			ChoiceIndex = choiceIndex;
			Card = card;
		}
	}

	public class OrgActionsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<ActionCardEntry> Hand  { get; private set; } = Array.Empty<ActionCardEntry>();
		public IReadOnlyList<ActionCardEntry> Deck  { get; private set; } = Array.Empty<ActionCardEntry>();
		public int HandSize { get; private set; }
		public void Set(System.Collections.Generic.List<ActionCardEntry> hand,
		                System.Collections.Generic.List<ActionCardEntry> deck,
		                int handSize) {
			HandSize = handSize;
			if (StateEquality.ListEquals(Hand, hand, StateEquality.ActionCardEntryEquals)
				&& StateEquality.ListEquals(Deck, deck, StateEquality.ActionCardEntryEquals)) {
				return;
			}
			Hand = hand; Deck = deck;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	// Not gated on any selected-country/selected-org validity the way CountryActionsState is -
	// this always reflects the given org's full country-card + org-card deck/hand (see
	// VisualStateConverter.BuildDebugOrgCardAvailability), independent of what's currently
	// selected on the map. Debug-menu-only; feeds DebugCardAvailabilityView.
	public class OrgCardAvailabilityState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public bool IsValid { get; private set; }
		public string OrgId { get; private set; } = "";
		public IReadOnlyList<ActionCardEntry> Hand { get; private set; } = Array.Empty<ActionCardEntry>();
		public IReadOnlyList<ActionCardEntry> Deck { get; private set; } = Array.Empty<ActionCardEntry>();

		public void Set(bool isValid, string orgId, List<ActionCardEntry> hand, List<ActionCardEntry> deck) {
			if (IsValid == isValid && OrgId == orgId
				&& StateEquality.ListEquals(Hand, hand, StateEquality.ActionCardEntryEquals)
				&& StateEquality.ListEquals(Deck, deck, StateEquality.ActionCardEntryEquals)) {
				return;
			}
			IsValid = isValid;
			OrgId = orgId;
			Hand = hand;
			Deck = deck;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class VisualResourceChangeEffect {
		public string EffectId { get; }
		public string ResourceId { get; }
		public string OwnerId { get; }
		public double Amount { get; }
		public VisualResourceChangeEffect(string effectId, string resourceId, string ownerId, double amount) {
			EffectId = effectId; ResourceId = resourceId; OwnerId = ownerId; Amount = amount;
		}
	}

	public class VisualEffectCollection : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public List<VisualResourceChangeEffect> Effects { get; private set; } = new List<VisualResourceChangeEffect>();

		public void Set(List<VisualResourceChangeEffect> effects) {
			var newEffects = effects ?? new List<VisualResourceChangeEffect>();
			if (StateEquality.ListEquals(Effects, newEffects, StateEquality.VisualResourceChangeEffectEquals)) {
				return;
			}
			Effects = newEffects;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public IReadOnlyList<VisualResourceChangeEffect> GetEffectsByResourceId(string resourceId) {
			var result = new List<VisualResourceChangeEffect>();
			foreach (var e in Effects) {
				if (e.ResourceId == resourceId) { result.Add(e); }
			}
			return result;
		}
	}

	public class CountryActionsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<ActionCardEntry> Hand { get; private set; } = Array.Empty<ActionCardEntry>();
		public IReadOnlyList<ActionCardEntry> Deck { get; private set; } = Array.Empty<ActionCardEntry>();
		public IReadOnlyList<CardDrawChoiceEntry> DrawChoices { get; private set; } = Array.Empty<CardDrawChoiceEntry>();
		public int HandSize { get; private set; }
		public bool HasPendingDraw { get; private set; }
		public bool CanStartDraw { get; private set; }
		public DateTime CurrentTime { get; private set; }

		public void Set(
			List<ActionCardEntry> hand,
			List<ActionCardEntry> deck,
			List<CardDrawChoiceEntry> drawChoices,
			int handSize,
			bool hasPendingDraw,
			bool canStartDraw,
			DateTime currentTime) {
			CurrentTime = currentTime;
			if (StateEquality.ListEquals(Hand, hand, StateEquality.ActionCardEntryEquals)
				&& StateEquality.ListEquals(Deck, deck, StateEquality.ActionCardEntryEquals)
				&& StateEquality.ListEquals(DrawChoices, drawChoices, StateEquality.CardDrawChoiceEntryEquals)
				&& HandSize == handSize
				&& HasPendingDraw == hasPendingDraw
				&& CanStartDraw == canStartDraw) {
				return;
			}
			Hand = hand;
			Deck = deck;
			DrawChoices = drawChoices;
			HandSize = handSize;
			HasPendingDraw = hasPendingDraw;
			CanStartDraw = canStartDraw;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class SaveResultState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool Success { get; private set; }
		public string? ErrorType { get; private set; }

		public void Set(bool success, string? errorType) {
			Success = success;
			ErrorType = errorType;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class ProvinceOwnershipState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyDictionary<string, string> OwnerByProvinceId { get; private set; } = new Dictionary<string, string>();
		public string RecentProvinceId { get; private set; } = "";
		public string RecentOldOwnerId { get; private set; } = "";
		public string RecentNewOwnerId { get; private set; } = "";

		public void Set(
			IReadOnlyDictionary<string, string> ownerByProvinceId,
			string recentProvinceId = "",
			string recentOldOwnerId = "",
			string recentNewOwnerId = "") {
			var equal = StateEquality.DictionaryContentEquals(OwnerByProvinceId, ownerByProvinceId);
			RecentProvinceId = recentProvinceId;
			RecentOldOwnerId = recentOldOwnerId;
			RecentNewOwnerId = recentNewOwnerId;
			if (equal) {
				return;
			}
			OwnerByProvinceId = ownerByProvinceId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class ProvinceOccupationState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyDictionary<string, string> OccupierByProvinceId { get; private set; } = new Dictionary<string, string>();
		public string RecentProvinceId { get; private set; } = "";
		public string RecentOldOccupierId { get; private set; } = "";
		public string RecentNewOccupierId { get; private set; } = "";

		public void Set(
			IReadOnlyDictionary<string, string> occupierByProvinceId,
			string recentProvinceId = "",
			string recentOldOccupierId = "",
			string recentNewOccupierId = "") {
			var equal = StateEquality.DictionaryContentEquals(OccupierByProvinceId, occupierByProvinceId);
			RecentProvinceId = recentProvinceId;
			RecentOldOccupierId = recentOldOccupierId;
			RecentNewOccupierId = recentNewOccupierId;
			if (equal) {
				return;
			}
			OccupierByProvinceId = occupierByProvinceId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class CountryScoreState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyDictionary<string, double> ScoreByCountryId { get; private set; } = new Dictionary<string, double>();

		public void Set(IReadOnlyDictionary<string, double> scoreByCountryId) {
			if (StateEquality.DictionaryContentEquals(ScoreByCountryId, scoreByCountryId)) {
				return;
			}
			ScoreByCountryId = scoreByCountryId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class LeaderboardEntryState {
		public int Place { get; }
		public string EntityId { get; }
		public string DisplayName { get; }
		public double Score { get; }

		public LeaderboardEntryState(int place, string entityId, string displayName, double score) {
			Place = place;
			EntityId = entityId;
			DisplayName = displayName;
			Score = score;
		}
	}

	public class LeaderboardState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyList<LeaderboardEntryState> Organizations { get; private set; } = Array.Empty<LeaderboardEntryState>();
		public IReadOnlyList<LeaderboardEntryState> Countries { get; private set; } = Array.Empty<LeaderboardEntryState>();

		public void Set(List<LeaderboardEntryState> organizations, List<LeaderboardEntryState> countries) {
			if (StateEquality.ListEquals(Organizations, organizations, StateEquality.LeaderboardEntryStateEquals)
				&& StateEquality.ListEquals(Countries, countries, StateEquality.LeaderboardEntryStateEquals)) {
				return;
			}
			Organizations = organizations;
			Countries = countries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class GoalProgressEntryState {
		public WinConditionHintKind Kind { get; }
		public double ConfigValue { get; }
		public double Current { get; }
		public double Target { get; }
		public int AvailableCountryCount { get; }

		public GoalProgressEntryState(
			WinConditionHintKind kind,
			double configValue,
			double current,
			double target,
			int availableCountryCount) {
			Kind = kind;
			ConfigValue = configValue;
			Current = current;
			Target = target;
			AvailableCountryCount = availableCountryCount;
		}
	}

	public class GoalsOrgEntryState {
		public string OrgId { get; }
		public IReadOnlyList<GoalProgressEntryState> Goals { get; }

		public GoalsOrgEntryState(string orgId, IReadOnlyList<GoalProgressEntryState> goals) {
			OrgId = orgId;
			Goals = goals;
		}
	}

	public class GoalsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyList<GoalsOrgEntryState> Organizations { get; private set; } = Array.Empty<GoalsOrgEntryState>();

		public void Set(List<GoalsOrgEntryState> organizations) {
			if (StateEquality.ListEquals(Organizations, organizations, StateEquality.GoalsOrgEntryStateEquals)) {
				return;
			}
			Organizations = organizations;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class ActiveTaskRewardState {
		public string ResourceId { get; }
		public double Amount { get; }

		public ActiveTaskRewardState(string resourceId, double amount) {
			ResourceId = resourceId;
			Amount = amount;
		}
	}

	public class ActiveTaskEntryState {
		public string TaskId { get; }
		public string NameKey { get; }
		public string DescKey { get; }
		public IReadOnlyList<ActiveTaskRewardState> Rewards { get; }
		public bool IsTutorial { get; }
		public string HighlightTargetId { get; }

		public ActiveTaskEntryState(
			string taskId,
			string nameKey,
			string descKey,
			IReadOnlyList<ActiveTaskRewardState> rewards,
			bool isTutorial = false,
			string highlightTargetId = "") {
			TaskId = taskId;
			NameKey = nameKey;
			DescKey = descKey;
			Rewards = rewards;
			IsTutorial = isTutorial;
			HighlightTargetId = highlightTargetId ?? "";
		}
	}

	public class ActiveTasksState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyList<ActiveTaskEntryState> Tasks { get; private set; } = Array.Empty<ActiveTaskEntryState>();

		public void Set(List<ActiveTaskEntryState> tasks) {
			if (StateEquality.ListEquals(Tasks, tasks, StateEquality.ActiveTaskEntryStateEquals)) {
				return;
			}
			Tasks = tasks;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class WarIconEntryState {
		public string WarId { get; }
		public double Progress { get; }
		public string AttackerCountryId { get; }
		public string DefenderCountryId { get; }

		public WarIconEntryState(string warId, double progress, string attackerCountryId, string defenderCountryId) {
			WarId = warId;
			Progress = progress;
			AttackerCountryId = attackerCountryId;
			DefenderCountryId = defenderCountryId;
		}
	}

	public class WarIconsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyList<WarIconEntryState> Entries { get; private set; } = Array.Empty<WarIconEntryState>();

		public void Set(List<WarIconEntryState> entries) {
			if (StateEquality.ListEquals(Entries, entries, StateEquality.WarIconEntryStateEquals)) {
				return;
			}
			Entries = entries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class WarProgressHistoryEntryState {
		public string EffectId { get; }
		public double AppliedDelta { get; }
		public DateTime Timestamp { get; }

		public WarProgressHistoryEntryState(string effectId, double appliedDelta, DateTime timestamp) {
			EffectId = effectId;
			AppliedDelta = appliedDelta;
			Timestamp = timestamp;
		}
	}

	public class WarSideStatsState {
		public string CountryId { get; }
		public double RecruitsAvailable { get; }
		public double TroopsInBattles { get; }
		public double Casualties { get; }
		public double Damage { get; }
		public double Durability { get; }
		public double DamageBase { get; }
		public double DamageRulerBonus { get; }
		public double DamageAdvisorBonus { get; }
		public double DamageBonusPercent { get; }
		public IReadOnlyList<EffectStateEntry> DamageBonusEffects { get; }
		public double DurabilityBase { get; }
		public double DurabilityRulerBonus { get; }
		public double DurabilityAdvisorBonus { get; }

		public WarSideStatsState(
			string countryId,
			double recruitsAvailable,
			double troopsInBattles,
			double casualties,
			double damage,
			double durability,
			double damageBase = 0,
			double damageRulerBonus = 0,
			double damageAdvisorBonus = 0,
			double damageBonusPercent = 0,
			IReadOnlyList<EffectStateEntry>? damageBonusEffects = null,
			double durabilityBase = 0,
			double durabilityRulerBonus = 0,
			double durabilityAdvisorBonus = 0) {
			CountryId = countryId;
			RecruitsAvailable = recruitsAvailable;
			TroopsInBattles = troopsInBattles;
			Casualties = casualties;
			Damage = damage;
			Durability = durability;
			DamageBase = damageBase;
			DamageRulerBonus = damageRulerBonus;
			DamageAdvisorBonus = damageAdvisorBonus;
			DamageBonusPercent = damageBonusPercent;
			DamageBonusEffects = damageBonusEffects ?? Array.Empty<EffectStateEntry>();
			DurabilityBase = durabilityBase;
			DurabilityRulerBonus = durabilityRulerBonus;
			DurabilityAdvisorBonus = durabilityAdvisorBonus;
		}

		public static WarSideStatsState Empty { get; } = new WarSideStatsState("", 0, 0, 0, 0, 0);
	}

	public class WarBattleRowState {
		public string BattleId { get; }
		public string ProvinceId { get; }
		public bool IsFinished { get; }
		public string WinnerCountryId { get; }
		public WarParticipantKind WinnerSide { get; }
		public double AttackerCasualties { get; }
		public double DefenderCasualties { get; }
		public double Progress { get; }
		public double AttackerTroops { get; }
		public double DefenderTroops { get; }

		public WarBattleRowState(
			string battleId,
			string provinceId,
			bool isFinished,
			string winnerCountryId,
			WarParticipantKind winnerSide,
			double attackerCasualties,
			double defenderCasualties,
			double progress,
			double attackerTroops,
			double defenderTroops) {
			BattleId = battleId;
			ProvinceId = provinceId;
			IsFinished = isFinished;
			WinnerCountryId = winnerCountryId;
			WinnerSide = winnerSide;
			AttackerCasualties = attackerCasualties;
			DefenderCasualties = defenderCasualties;
			Progress = progress;
			AttackerTroops = attackerTroops;
			DefenderTroops = defenderTroops;
		}
	}

	public class SelectedWarState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		string _pendingWarId = "";

		public bool IsValid { get; private set; }
		public string WarId { get; private set; } = "";
		public double Progress { get; private set; }
		public IReadOnlyList<WarProgressHistoryEntryState> History { get; private set; } = Array.Empty<WarProgressHistoryEntryState>();
		public WarSideStatsState Attacker { get; private set; } = WarSideStatsState.Empty;
		public WarSideStatsState Defender { get; private set; } = WarSideStatsState.Empty;
		public IReadOnlyList<WarBattleRowState> Battles { get; private set; } = Array.Empty<WarBattleRowState>();

		internal string PendingWarId => _pendingWarId;

		public void RequestOpen(string warId) {
			_pendingWarId = warId ?? "";
		}

		public void Clear() {
			if (string.IsNullOrEmpty(_pendingWarId) && !IsValid && WarId == "") {
				return;
			}
			_pendingWarId = "";
			SetInvalid();
		}

		public void SetInvalid() {
			bool alreadyInvalid = !IsValid
				&& WarId == ""
				&& Progress == 0
				&& History.Count == 0
				&& StateEquality.WarSideStatsStateEquals(Attacker, WarSideStatsState.Empty)
				&& StateEquality.WarSideStatsStateEquals(Defender, WarSideStatsState.Empty)
				&& Battles.Count == 0;
			if (alreadyInvalid) {
				// Opening a missing/stopped war while already invalid would otherwise no-op;
				// notify so the document can Hide when a pending request is outstanding.
				if (!string.IsNullOrEmpty(_pendingWarId)) {
					PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
				}
				return;
			}
			Set(false, "", 0, Array.Empty<WarProgressHistoryEntryState>(), WarSideStatsState.Empty, WarSideStatsState.Empty, Array.Empty<WarBattleRowState>());
		}

		public void Set(
			bool isValid,
			string warId,
			double progress,
			IReadOnlyList<WarProgressHistoryEntryState> history,
			WarSideStatsState attacker,
			WarSideStatsState defender,
			IReadOnlyList<WarBattleRowState> battles) {
			if (IsValid == isValid
				&& WarId == warId
				&& Progress == progress
				&& StateEquality.ListEquals(History, history, StateEquality.WarProgressHistoryEntryStateEquals)
				&& StateEquality.WarSideStatsStateEquals(Attacker, attacker)
				&& StateEquality.WarSideStatsStateEquals(Defender, defender)
				&& StateEquality.ListEquals(Battles, battles, StateEquality.WarBattleRowStateEquals)) {
				return;
			}
			IsValid = isValid;
			WarId = warId;
			Progress = progress;
			History = history;
			Attacker = attacker;
			Defender = defender;
			Battles = battles;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class WarGoldRecipientState {
		public OwnerType OwnerType { get; }
		public string OwnerId { get; }
		public double Amount { get; }

		public WarGoldRecipientState(OwnerType ownerType, string ownerId, double amount) {
			OwnerType = ownerType;
			OwnerId = ownerId;
			Amount = amount;
		}
	}

	public class WarControlDeltaState {
		public string CountryId { get; }
		public string OrgId { get; }
		public int Delta { get; }
		public int TotalAfter { get; }

		public WarControlDeltaState(string countryId, string orgId, int delta, int totalAfter) {
			CountryId = countryId;
			OrgId = orgId;
			Delta = delta;
			TotalAfter = totalAfter;
		}
	}

	public class WarProvinceTransferState {
		public string ProvinceId { get; }
		public string OldOwnerCountryId { get; }
		public string NewOwnerCountryId { get; }

		public WarProvinceTransferState(string provinceId, string oldOwnerCountryId, string newOwnerCountryId) {
			ProvinceId = provinceId;
			OldOwnerCountryId = oldOwnerCountryId;
			NewOwnerCountryId = newOwnerCountryId;
		}
	}

	public class WarResultSnapshotState {
		public string WarId { get; }
		public string AttackerCountryId { get; }
		public string DefenderCountryId { get; }
		public string WinnerCountryId { get; }
		public string LoserCountryId { get; }
		public double Progress { get; }
		public bool ShouldPause { get; }
		public double GoldTaken { get; }
		public IReadOnlyList<WarGoldRecipientState> GoldRecipients { get; }
		public IReadOnlyList<WarControlDeltaState> ControlDeltas { get; }
		public IReadOnlyList<WarProvinceTransferState> TransferredProvinces { get; }
		public IReadOnlyList<WarProgressHistoryEntryState> History { get; }
		public WarSideStatsState Attacker { get; }
		public WarSideStatsState Defender { get; }
		public IReadOnlyList<WarBattleRowState> Battles { get; }

		public WarResultSnapshotState(
			string warId,
			string attackerCountryId,
			string defenderCountryId,
			string winnerCountryId,
			string loserCountryId,
			double progress,
			bool shouldPause,
			double goldTaken,
			IReadOnlyList<WarGoldRecipientState> goldRecipients,
			IReadOnlyList<WarControlDeltaState> controlDeltas,
			IReadOnlyList<WarProvinceTransferState> transferredProvinces,
			IReadOnlyList<WarProgressHistoryEntryState> history,
			WarSideStatsState attacker,
			WarSideStatsState defender,
			IReadOnlyList<WarBattleRowState> battles) {
			WarId = warId;
			AttackerCountryId = attackerCountryId;
			DefenderCountryId = defenderCountryId;
			WinnerCountryId = winnerCountryId;
			LoserCountryId = loserCountryId;
			Progress = progress;
			ShouldPause = shouldPause;
			GoldTaken = goldTaken;
			GoldRecipients = goldRecipients;
			ControlDeltas = controlDeltas;
			TransferredProvinces = transferredProvinces;
			History = history;
			Attacker = attacker;
			Defender = defender;
			Battles = battles;
		}
	}

	public class WarResultsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		readonly List<WarResultSnapshotState> _queue = new();

		public IReadOnlyList<WarResultSnapshotState> Entries => _queue;

		public void Enqueue(WarResultSnapshotState snapshot) {
			_queue.Add(snapshot);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public bool TryPeek(out WarResultSnapshotState? snapshot) {
			if (_queue.Count == 0) {
				snapshot = null;
				return false;
			}
			snapshot = _queue[0];
			return true;
		}

		public void AcknowledgeCurrent() {
			if (_queue.Count == 0) {
				return;
			}
			_queue.RemoveAt(0);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class CountryDestroyedSnapshotState {
		public string CountryId { get; }

		public CountryDestroyedSnapshotState(string countryId) {
			CountryId = countryId;
		}
	}

	public class CountryDestroyedResultsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		readonly List<CountryDestroyedSnapshotState> _queue = new();

		public IReadOnlyList<CountryDestroyedSnapshotState> Entries => _queue;

		public void Enqueue(CountryDestroyedSnapshotState snapshot) {
			_queue.Add(snapshot);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public bool TryPeek(out CountryDestroyedSnapshotState? snapshot) {
			if (_queue.Count == 0) {
				snapshot = null;
				return false;
			}
			snapshot = _queue[0];
			return true;
		}

		public void AcknowledgeCurrent() {
			if (_queue.Count == 0) {
				return;
			}
			_queue.RemoveAt(0);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class OrgDestroyedSnapshotState {
		public string OrganizationId { get; }

		public OrgDestroyedSnapshotState(string organizationId) {
			OrganizationId = organizationId;
		}
	}

	public class OrgDestroyedResultsState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		readonly List<OrgDestroyedSnapshotState> _queue = new();

		public IReadOnlyList<OrgDestroyedSnapshotState> Entries => _queue;

		public void Enqueue(OrgDestroyedSnapshotState snapshot) {
			_queue.Add(snapshot);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public bool TryPeek(out OrgDestroyedSnapshotState? snapshot) {
			if (_queue.Count == 0) {
				snapshot = null;
				return false;
			}
			snapshot = _queue[0];
			return true;
		}

		public void AcknowledgeCurrent() {
			if (_queue.Count == 0) {
				return;
			}
			_queue.RemoveAt(0);
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class SelectedProvinceState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string ProvinceId { get; private set; } = "";
		public CountryResourcesState Resources { get; } = new CountryResourcesState();

		public void Set(bool isValid, string provinceId) {
			if (IsValid == isValid && ProvinceId == provinceId) {
				return;
			}
			IsValid = isValid;
			ProvinceId = provinceId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public enum GameLogEntryKind {
		Control,
		Opinion,
		NewCharacter,
		Relation,
		War,
		WarResolved
	}

	public class GameLogEntry {
		public long SequenceId { get; }          // monotonic, for UI-side identity/diffing
		public GameLogEntryKind Kind { get; }
		public string OrgId { get; }             // acting org; "" for the country-role NewCharacter variant
		public string CountryId { get; }         // target/home country; "" when not applicable
		public string CharacterId { get; }
		public string RoleId { get; }
		public string[] NamePartKeys { get; }    // snapshot, not a re-lookup key
		public double Delta { get; }             // Control/Opinion only; amount just applied
		public double Total { get; }             // Control/Opinion only; new resulting total (Opinion: clamped to [-100,100])
		public bool IsOrgRole { get; }           // NewCharacter only: true = OrgId set/CountryId empty
		public string TargetCountryId { get; }   // Relation/War/WarResolved: the other country involved (WarResolved: loser; CountryId is the winner)
		public RelationKind RelationKind { get; } // Relation only

		public GameLogEntry(long sequenceId, GameLogEntryKind kind, string orgId, string countryId,
			string characterId, string roleId, string[] namePartKeys, double delta, double total, bool isOrgRole,
			string targetCountryId = "", RelationKind relationKind = default) {
			SequenceId = sequenceId;
			Kind = kind;
			OrgId = orgId;
			CountryId = countryId;
			CharacterId = characterId;
			RoleId = roleId;
			NamePartKeys = namePartKeys;
			Delta = delta;
			Total = total;
			IsOrgRole = isOrgRole;
			TargetCountryId = targetCountryId;
			RelationKind = relationKind;
		}
	}

	public class GameLogState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<GameLogEntry> Entries { get; private set; } = Array.Empty<GameLogEntry>();
		public void Set(List<GameLogEntry> entries) {
			if (StateEquality.ListEquals(Entries, entries, StateEquality.GameLogEntryEquals)) {
				return;
			}
			Entries = entries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class EndGameComparisonRowState {
		public int Place { get; }
		public string ComparisonElementId { get; }
		public bool IsPlayer { get; }
		public string DisplayName { get; }
		public double Score { get; }

		public EndGameComparisonRowState(int place, string comparisonElementId, bool isPlayer, string displayName, double score) {
			Place = place;
			ComparisonElementId = comparisonElementId;
			IsPlayer = isPlayer;
			DisplayName = displayName;
			Score = score;
		}
	}

	public enum GameResult {
		InProgress,
		Win,
		Lose
	}

	public enum WinConditionHintKind {
		TotalControl,
		FullControlCountries,
		ScoreGoal,
		LastOrgStanding
	}

	public class WinConditionHintRowState {
		public WinConditionHintKind Kind { get; }
		public double Value { get; }
		public int AvailableCountryCount { get; }

		public WinConditionHintRowState(WinConditionHintKind kind, double value, int availableCountryCount) {
			Kind = kind;
			Value = value;
			AvailableCountryCount = availableCountryCount;
		}
	}

	public class CharacterCardHintRowState {
		public string ActionId { get; }
		public int Threshold { get; }
		public bool IsMet { get; set; }

		public CharacterCardHintRowState(string actionId, int threshold) {
			ActionId = actionId;
			Threshold = threshold;
		}
	}

	public class WinConditionHintState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsAvailable { get; private set; }
		public bool IsAlternativeGroup { get; private set; }
		public IReadOnlyList<WinConditionHintRowState> Rows { get; private set; } = Array.Empty<WinConditionHintRowState>();

		public void Set(bool isAvailable, bool isAlternativeGroup, List<WinConditionHintRowState> rows) {
			IsAvailable = isAvailable;
			IsAlternativeGroup = isAlternativeGroup;
			Rows = rows;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class GameCompletionState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsCompleted { get; private set; }
		public string WinnerOrganizationId { get; private set; } = "";
		public GameResult Result { get; private set; }

		public void Set(bool isCompleted, string winnerOrganizationId, GameResult result) {
			IsCompleted = isCompleted;
			WinnerOrganizationId = winnerOrganizationId;
			Result = result;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class VisualState {
		public SelectedCountryState SelectedCountry { get; } = new SelectedCountryState();
		public TimeState Time { get; } = new TimeState();
		public LocaleState Locale { get; } = new LocaleState();
		public PlayerOrganizationState PlayerOrganization { get; } = new PlayerOrganizationState();
		public SelectedOrganizationState SelectedOrganization { get; } = new SelectedOrganizationState();
		public CountryResourcesState OrgLensOrganizationResources { get; } = new CountryResourcesState();
		public MapLensState MapLens { get; } = new MapLensState();
		public OrgMapState OrgMap { get; } = new OrgMapState();
		public WorldCountriesState WorldCountries { get; } = new WorldCountriesState();
		public VisualEffectCollection LastFrameEffects { get; } = new VisualEffectCollection();
		public SaveResultState SaveResult { get; } = new SaveResultState();
		public ProvinceOwnershipState ProvinceOwnership { get; } = new ProvinceOwnershipState();
		public ProvinceOccupationState ProvinceOccupation { get; } = new ProvinceOccupationState();
		public SelectedProvinceState SelectedProvince { get; } = new SelectedProvinceState();
		public CountryScoreState CountryScore { get; } = new CountryScoreState();
		public ActiveTasksState ActiveTasks { get; } = new ActiveTasksState();
		public WarIconsState WarIcons { get; } = new WarIconsState();
		public SelectedWarState SelectedWar { get; } = new SelectedWarState();
		public WarResultsState WarResults { get; } = new WarResultsState();
		public CountryDestroyedResultsState CountryDestroyedResults { get; } = new CountryDestroyedResultsState();
		public OrgDestroyedResultsState OrgDestroyedResults { get; } = new OrgDestroyedResultsState();
		public GameLogState GameLog { get; } = new GameLogState();
		public GameCompletionState GameCompletion { get; } = new GameCompletionState();
		public WinConditionHintState WinConditionHint { get; } = new WinConditionHintState();
	}
}
