using System;
using System.Collections.Generic;
using System.ComponentModel;
using GS.Game.Components;

namespace GS.Main {
	public class EffectStateEntry {
		public string EffectId { get; }
		public double Value { get; }
		public PayType PayType { get; }

		public EffectStateEntry(string effectId, double value, PayType payType) {
			EffectId = effectId;
			Value = value;
			PayType = payType;
		}
	}

	public class ResourceStateEntry {
		public string ResourceId { get; }
		public double Value { get; }
		public IReadOnlyList<EffectStateEntry> Effects { get; }

		public ResourceStateEntry(string resourceId, double value, IReadOnlyList<EffectStateEntry> effects) {
			ResourceId = resourceId;
			Value = value;
			Effects = effects;
		}
	}

	public class CountryResourcesState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string CountryId { get; private set; } = "";
		public IReadOnlyList<ResourceStateEntry> Resources { get; private set; } = Array.Empty<ResourceStateEntry>();

		public void Set(bool isValid, string countryId, List<ResourceStateEntry> resources) {
			IsValid = isValid;
			CountryId = countryId;
			Resources = resources;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}
}
