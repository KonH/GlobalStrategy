using System;
using System.ComponentModel;

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

	public class VisualState {
		public SelectedCountryState SelectedCountry { get; } = new SelectedCountryState();
		public PlayerCountryState PlayerCountry { get; } = new PlayerCountryState();
		public TimeState Time { get; } = new TimeState();
		public LocaleState Locale { get; } = new LocaleState();
		public CountryResourcesState PlayerResources { get; } = new CountryResourcesState();
		public CountryResourcesState SelectedResources { get; } = new CountryResourcesState();
		public PlayerOrganizationState PlayerOrganization { get; } = new PlayerOrganizationState();
		public SelectedOrganizationState SelectedOrganization { get; } = new SelectedOrganizationState();
	}
}
