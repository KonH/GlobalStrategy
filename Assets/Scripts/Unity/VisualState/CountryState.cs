using System;

namespace GS.Unity.VisualState {
	public class CountryState {
		string _countryName;
		bool _isValid;

		public event Action<CountryState> OnChanged;

		public string CountryName => _countryName;
		public bool IsValid => _isValid;

		public void Set(string countryName, bool isValid) {
			_countryName = countryName;
			_isValid = isValid;
			OnChanged?.Invoke(this);
		}
	}
}
