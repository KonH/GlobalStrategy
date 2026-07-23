using System;
using System.Collections.Generic;
using System.ComponentModel;
using GS.Game.Commands;

namespace GS.Main {
	public class SelectedCountryState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string CountryId { get; private set; } = "";
		public CountryResourcesState Resources { get; } = new CountryResourcesState();
		public CountryControlState Control { get; } = new CountryControlState();
		public CountryCharactersState Characters { get; } = new CountryCharactersState();
		public CountryActionsState CountryActions { get; } = new CountryActionsState();

		public void Set(bool isValid, string countryId) {
			IsValid = isValid;
			CountryId = countryId;
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
		public CountryResourcesState Resources { get; } = new CountryResourcesState();
		public OrgCharactersState Characters { get; } = new OrgCharactersState();
		public OrgActionsState Actions { get; } = new OrgActionsState();

		public void Set(bool isValid, string orgId, string displayName, string hqCountryId = "") {
			IsValid = isValid;
			OrgId = orgId;
			DisplayName = displayName;
			HqCountryId = hqCountryId;
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
			UsedControl.SetActual(used);
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
		public CharacterStateEntry(string characterId, string roleId, string[] namePartKeys, IReadOnlyList<SkillEntry> skills, AnimatableInt opinion) {
			CharacterId = characterId;
			RoleId = roleId;
			NamePartKeys = namePartKeys;
			Skills = skills;
			Opinion = opinion;
		}
	}

	public class CountryCharactersState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<CharacterStateEntry> Characters { get; private set; } = Array.Empty<CharacterStateEntry>();
		public void Set(List<CharacterStateEntry> characters) {
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
			Entries = entries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class DiscoveredCountriesState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public System.Collections.Generic.HashSet<string> CountryIds { get; private set; } = new System.Collections.Generic.HashSet<string>();
		public string RecentlyDiscovered { get; private set; } = "";

		public void Set(System.Collections.Generic.HashSet<string> ids, string recentlyDiscovered = "") {
			CountryIds = ids;
			RecentlyDiscovered = recentlyDiscovered;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}

		public void ClearRecentlyDiscovered() {
			RecentlyDiscovered = "";
		}
	}

	public class ActionCardEntry {
		public string ActionId        { get; }
		public int    SlotIndex       { get; }
		public bool   IsInHand        { get; }
		public bool   IsUnplayable    { get; }
		public string UnplayableReason { get; }
		public ActionCardEntry(string actionId, int slotIndex, bool isInHand, bool isUnplayable = false, string unplayableReason = "") {
			ActionId = actionId; SlotIndex = slotIndex; IsInHand = isInHand;
			IsUnplayable = isUnplayable; UnplayableReason = unplayableReason;
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
			Hand = hand; Deck = deck; HandSize = handSize;
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
			Effects = effects ?? new List<VisualResourceChangeEffect>();
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
		public int HandSize { get; private set; }
		public DateTime CurrentTime { get; private set; }

		public void Set(List<ActionCardEntry> hand, List<ActionCardEntry> deck, int handSize, DateTime currentTime) {
			Hand = hand; Deck = deck; HandSize = handSize; CurrentTime = currentTime;
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
			OwnerByProvinceId = ownerByProvinceId;
			RecentProvinceId = recentProvinceId;
			RecentOldOwnerId = recentOldOwnerId;
			RecentNewOwnerId = recentNewOwnerId;
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
			OccupierByProvinceId = occupierByProvinceId;
			RecentProvinceId = recentProvinceId;
			RecentOldOccupierId = recentOldOccupierId;
			RecentNewOccupierId = recentNewOccupierId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class CountryScoreState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public IReadOnlyDictionary<string, double> ScoreByCountryId { get; private set; } = new Dictionary<string, double>();

		public void Set(IReadOnlyDictionary<string, double> scoreByCountryId) {
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
			Organizations = organizations;
			Countries = countries;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class SelectedProvinceState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string ProvinceId { get; private set; } = "";
		public CountryResourcesState Resources { get; } = new CountryResourcesState();

		public void Set(bool isValid, string provinceId) {
			IsValid = isValid;
			ProvinceId = provinceId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public enum GameLogEntryKind {
		Discovery,
		Control,
		Opinion,
		NewCharacter
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

		public GameLogEntry(long sequenceId, GameLogEntryKind kind, string orgId, string countryId,
			string characterId, string roleId, string[] namePartKeys, double delta, double total, bool isOrgRole) {
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
		}
	}

	public class GameLogState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;
		public IReadOnlyList<GameLogEntry> Entries { get; private set; } = Array.Empty<GameLogEntry>();
		public void Set(List<GameLogEntry> entries) {
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
		FullControlCountries
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
		public MapLensState MapLens { get; } = new MapLensState();
		public OrgMapState OrgMap { get; } = new OrgMapState();
		public DiscoveredCountriesState DiscoveredCountries { get; } = new DiscoveredCountriesState();
		public VisualEffectCollection LastFrameEffects { get; } = new VisualEffectCollection();
		public SaveResultState SaveResult { get; } = new SaveResultState();
		public ProvinceOwnershipState ProvinceOwnership { get; } = new ProvinceOwnershipState();
		public ProvinceOccupationState ProvinceOccupation { get; } = new ProvinceOccupationState();
		public SelectedProvinceState SelectedProvince { get; } = new SelectedProvinceState();
		public CountryScoreState CountryScore { get; } = new CountryScoreState();
		public LeaderboardState Leaderboard { get; } = new LeaderboardState();
		public GameLogState GameLog { get; } = new GameLogState();
		public GameCompletionState GameCompletion { get; } = new GameCompletionState();
		public WinConditionHintState WinConditionHint { get; } = new WinConditionHintState();
	}
}
