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
		public CountryInfluenceState Influence { get; } = new CountryInfluenceState();
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

	public class OrgInfluenceEntry {
		public string OrgId { get; }
		public string DisplayName { get; }
		public int Influence { get; }
		public int BaseInfluence { get; }
		public int PermanentInfluence { get; }
		public double EstimatedMonthlyGold { get; }

		public OrgInfluenceEntry(string orgId, string displayName, int influence, int baseInfluence, int permanentInfluence, double estimatedMonthlyGold) {
			OrgId = orgId;
			DisplayName = displayName;
			Influence = influence;
			BaseInfluence = baseInfluence;
			PermanentInfluence = permanentInfluence;
			EstimatedMonthlyGold = estimatedMonthlyGold;
		}
	}

	public class CountryInfluenceState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public AnimatableInt UsedInfluence { get; } = new AnimatableInt();
		public int PoolSize => 100;
		public IReadOnlyList<OrgInfluenceEntry> OrgEntries { get; private set; } = Array.Empty<OrgInfluenceEntry>();

		public void Set(int used, List<OrgInfluenceEntry> entries) {
			UsedInfluence.SetActual(used);
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
		public float InfluenceRatio { get; }
		public OrgCountryEntry(string countryId, string topOrgId, float influenceRatio) {
			CountryId = countryId;
			TopOrgId = topOrgId;
			InfluenceRatio = influenceRatio;
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
	}
}
