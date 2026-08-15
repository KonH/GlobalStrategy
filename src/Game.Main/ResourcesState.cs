using System;
using System.Collections.Generic;
using System.ComponentModel;
using GS.Game.Components;

namespace GS.Main {
	public class EffectStateEntry {
		public string EffectId { get; }
		public double Value { get; }
		public PayType PayType { get; }
		public double MaxTotal { get; }
		public string OrgDisplayName { get; }
		public BaseIncomeBreakdownState? BaseIncomeBreakdown { get; }

		public EffectStateEntry(
			string effectId, double value, PayType payType, double maxTotal = 0, string orgDisplayName = "",
			BaseIncomeBreakdownState? baseIncomeBreakdown = null) {
			EffectId = effectId;
			Value = value;
			PayType = payType;
			MaxTotal = maxTotal;
			OrgDisplayName = orgDisplayName;
			BaseIncomeBreakdown = baseIncomeBreakdown;
		}
	}

	public class BaseIncomeBreakdownState {
		public double FlatBase { get; }
		public double Population { get; }
		public double PopulationContribution { get; }
		public int ProvinceCount { get; }
		public double ProvinceContribution { get; }
		public double AdvisorSkill { get; }
		public double AdvisorContribution { get; }

		public BaseIncomeBreakdownState(
			double flatBase,
			double population, double populationContribution,
			int provinceCount, double provinceContribution,
			double advisorSkill, double advisorContribution) {
			FlatBase = flatBase;
			Population = population;
			PopulationContribution = populationContribution;
			ProvinceCount = provinceCount;
			ProvinceContribution = provinceContribution;
			AdvisorSkill = advisorSkill;
			AdvisorContribution = advisorContribution;
		}
	}

	public class ResourceStateEntry {
		public string ResourceId { get; }
		public AnimatableDouble Value { get; }
		public IReadOnlyList<EffectStateEntry> Effects { get; }

		// Frozen at construction time, unlike Value.Actual: Value is a cached AnimatableDouble
		// reused across ticks by VisualStateConverter (keyed by resource id), so a live read of
		// Value.Actual from an older ResourceStateEntry already reflects whatever the *current*
		// tick just set it to (both the old and new entry point at the same mutable object).
		// Comparing that live field against itself in StateEquality.ResourceStateEntryEquals made
		// the equality check a no-op for value changes after the first tick. This snapshot captures
		// the actual value at the moment this specific entry was built, so old-vs-new comparisons
		// see the true before/after values.
		public double ActualSnapshot { get; }

		public ResourceStateEntry(string resourceId, AnimatableDouble value, IReadOnlyList<EffectStateEntry> effects) {
			ResourceId = resourceId;
			Value = value;
			Effects = effects;
			ActualSnapshot = value.Actual;
		}
	}

	public class ControlIncomeEntry {
		public string CountryId { get; }
		public double MonthlyGold { get; }

		public ControlIncomeEntry(string countryId, double monthlyGold) {
			CountryId = countryId;
			MonthlyGold = monthlyGold;
		}
	}

	public class CountryResourcesState : INotifyPropertyChanged {
		public event PropertyChangedEventHandler? PropertyChanged;

		public bool IsValid { get; private set; }
		public string CountryId { get; private set; } = "";
		public IReadOnlyList<ResourceStateEntry> Resources { get; private set; } = Array.Empty<ResourceStateEntry>();
		public IReadOnlyList<ControlIncomeEntry> ControlIncomes { get; private set; } = Array.Empty<ControlIncomeEntry>();

		public void Set(bool isValid, string countryId, List<ResourceStateEntry> resources,
				IReadOnlyList<ControlIncomeEntry>? controlIncomes = null) {
			var incomes = controlIncomes ?? Array.Empty<ControlIncomeEntry>();
			if (IsValid == isValid && CountryId == countryId
				&& StateEquality.ListEquals(Resources, resources, StateEquality.ResourceStateEntryEquals)
				&& StateEquality.ListEquals(ControlIncomes, incomes, StateEquality.ControlIncomeEntryEquals)) {
				return;
			}
			IsValid = isValid;
			CountryId = countryId;
			Resources = resources;
			ControlIncomes = incomes;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
		}
	}
}
