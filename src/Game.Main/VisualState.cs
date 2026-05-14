using System;
using System.Collections.Generic;
using System.ComponentModel;
using GS.Game.Commands;

namespace GS.Main {
	public class SelectedCountryState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string CountryId { get; private set; } = "";

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

	public class PlayerCountryState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string CountryId { get; private set; } = "";

		public void Set(bool isValid, string countryId) {
			IsValid = isValid;
			CountryId = countryId;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}

	public class PlayerOrganizationState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string OrgId { get; private set; } = "";
		public string DisplayName { get; private set; } = "";

		public void Set(bool isValid, string orgId, string displayName) {
			IsValid = isValid;
			OrgId = orgId;
			DisplayName = displayName;
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

		public int UsedInfluence { get; private set; }
		public int PoolSize => 100;
		public IReadOnlyList<OrgInfluenceEntry> OrgEntries { get; private set; } = Array.Empty<OrgInfluenceEntry>();

		public void Set(int used, List<OrgInfluenceEntry> entries) {
			UsedInfluence = used;
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
		public CharacterStateEntry(string characterId, string roleId, string[] namePartKeys, IReadOnlyList<SkillEntry> skills) {
			CharacterId = characterId;
			RoleId = roleId;
			NamePartKeys = namePartKeys;
			Skills = skills;
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

	public class VisualState {
		public SelectedCountryState SelectedCountry { get; } = new SelectedCountryState();
		public PlayerCountryState PlayerCountry { get; } = new PlayerCountryState();
		public TimeState Time { get; } = new TimeState();
		public LocaleState Locale { get; } = new LocaleState();
		public CountryResourcesState PlayerResources { get; } = new CountryResourcesState();
		public CountryResourcesState SelectedResources { get; } = new CountryResourcesState();
		public PlayerOrganizationState PlayerOrganization { get; } = new PlayerOrganizationState();
		public SelectedOrganizationState SelectedOrganization { get; } = new SelectedOrganizationState();
		public CountryInfluenceState SelectedInfluence { get; } = new CountryInfluenceState();
		public CountryCharactersState SelectedCharacters { get; } = new CountryCharactersState();
		public OrgCharactersState PlayerOrgCharacters { get; } = new OrgCharactersState();
		public MapLensState MapLens { get; } = new MapLensState();
		public OrgMapState OrgMap { get; } = new OrgMapState();
	}
}
