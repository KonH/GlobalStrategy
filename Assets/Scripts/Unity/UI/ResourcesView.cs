#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Unity.UI {
	public class ResourcesView {
		readonly VisualElement _container;
		readonly ILocalization _loc;
		readonly ResourceConfig _config;
		readonly TooltipSystem _tooltip;
		public ResourcesView(VisualElement container, ILocalization loc, ResourceConfig config, TooltipSystem tooltip) {
			_container = container;
			_loc = loc;
			_config = config;
			_tooltip = tooltip;
		}

		public void Refresh(CountryResourcesState state) {
			_container.Clear();
			if (!state.IsValid) {
				return;
			}
			foreach (string resourceId in _config.DisplayWhitelist) {
				var resource = FindResourceState(state, resourceId);
				if (resource == null) {
					continue;
				}

				ResourceChipBuilder.Elements chip = ResourceChipBuilder.Build();
				chip.Chip.AddToClassList("resource-row");
				if (_container.childCount > 0) {
					chip.Chip.AddToClassList("resource-row--spaced");
				}
				string iconClass = ResourceDisplayNaming.IconClass(resource.ResourceId);
				ResourceChipBuilder.Bind(chip, iconClass, FormatResourceValue(resource.Value.Display));
				chip.Label.AddToClassList("gs-label");

				var capturedResource = resource;
				var capturedState = state;
				_tooltip.RegisterTrigger(chip.Chip, capturedResource.ResourceId, ctx => BuildResourceTooltip(ctx, capturedResource, capturedState), new HashSet<string>());

				_container.Add(chip.Chip);
			}
		}

		static string FormatResourceValue(double value) {
			double roundedValue = Math.Round(value, MidpointRounding.AwayFromZero);
			double magnitude = Math.Abs(roundedValue);
			if (magnitude < 1_000) {
				return roundedValue.ToString("0", CultureInfo.InvariantCulture);
			}

			double divisor = magnitude < 1_000_000 ? 1_000 : 1_000_000;
			string suffix = magnitude < 1_000_000 ? "K" : "M";
			double scaledValue = Math.Round(roundedValue / divisor, MidpointRounding.AwayFromZero);
			if (suffix == "K" && Math.Abs(scaledValue) >= 1_000) {
				scaledValue = Math.Round(roundedValue / 1_000_000, MidpointRounding.AwayFromZero);
				suffix = "M";
			}

			return $"{scaledValue.ToString("0", CultureInfo.InvariantCulture)}{suffix}";
		}

		static ResourceStateEntry? FindResourceState(CountryResourcesState state, string resourceId) {
			foreach (var resource in state.Resources) {
				if (resource.ResourceId == resourceId) {
					return resource;
				}
			}
			return null;
		}

		VisualElement BuildResourceTooltip(TooltipContext ctx, ResourceStateEntry resource, CountryResourcesState state) {
			var root = TooltipBodyBuilder.NewRoot();

			var resDef = _config.FindResource(resource.ResourceId);
			string nameKey = ResourceDisplayNaming.NameKey(resource.ResourceId);
			TooltipBodyBuilder.AddHeader(root, _loc.TryGet(nameKey, resource.ResourceId));

			string descriptionKey = ResourceDisplayNaming.DescriptionKey(resource.ResourceId);
			string? description = _loc.TryGet(descriptionKey, null);
			if (description != null) {
				TooltipBodyBuilder.AddDescription(root, description);
			}

			double plusTotal = 0;
			double minusTotal = 0;
			double instantTotal = 0;

			foreach (var effect in resource.Effects) {
				if (effect.PayType == PayType.Monthly) {
					if (effect.Value > 0) {
						plusTotal += effect.Value;
					} else if (effect.Value < 0) {
						minusTotal += effect.Value;
					}
				} else {
					instantTotal += effect.Value;
				}
			}

			if (plusTotal > 0) {
				string plusText = $"+{plusTotal:F1}/month";
				Label plusRow = TooltipBodyBuilder.AddLine(root, plusText, TooltipBodyBuilder.LineTone.Positive, innerTrigger: true);

				string capturedId = resource.ResourceId;
				ctx.RegisterInnerTrigger(plusRow, $"{capturedId}.plus", innerCtx =>
					BuildMonthlyEffectList(innerCtx, plusText, resource, resDef, positiveOnly: true));
			}

			if (minusTotal < 0) {
				string minusText = $"{minusTotal:F1}/month";
				Label minusRow = TooltipBodyBuilder.AddLine(root, minusText, TooltipBodyBuilder.LineTone.Negative, innerTrigger: true);

				string capturedId = resource.ResourceId;
				ctx.RegisterInnerTrigger(minusRow, $"{capturedId}.minus", innerCtx =>
					BuildMonthlyEffectList(innerCtx, minusText, resource, resDef, positiveOnly: false));
			}

			if (instantTotal != 0) {
				string sign = instantTotal > 0 ? "+" : "";
				string instantText = $"{sign}{instantTotal:F1} instant";
				Label instantRow = TooltipBodyBuilder.AddLine(
					root, instantText,
					instantTotal > 0 ? TooltipBodyBuilder.LineTone.Positive : TooltipBodyBuilder.LineTone.Negative,
					innerTrigger: true);

				string capturedId = resource.ResourceId;
				ctx.RegisterInnerTrigger(instantRow, $"{capturedId}.instant", innerCtx =>
					BuildInstantEffectList(innerCtx, instantText, resource, resDef));
			}

			// Control income rows (gold resource only)
			if (resource.ResourceId == ResourceDefinitions.Gold && state.ControlIncomes.Count > 0) {
				double controlTotal = 0;
				foreach (var inc in state.ControlIncomes) {
					controlTotal += inc.MonthlyGold;
				}
				string controlText = $"+{controlTotal:F1}/month";
				Label controlRow = TooltipBodyBuilder.AddLine(root, controlText, TooltipBodyBuilder.LineTone.Positive, innerTrigger: true);

				var capturedState = state;
				ctx.RegisterInnerTrigger(controlRow, "gold.control", innerCtx =>
					BuildControlIncomeList(controlText, capturedState));
			}

			return root;
		}

		VisualElement BuildControlIncomeList(string headerText, CountryResourcesState state) {
			var root = TooltipBodyBuilder.NewRoot();
			TooltipBodyBuilder.AddHeader(root, headerText);

			foreach (var inc in state.ControlIncomes) {
				string countryName = _loc.Get($"country_name.{inc.CountryId}");
				TooltipBodyBuilder.AddLine(root, $"Control ({countryName}): +{inc.MonthlyGold:F1}/month", TooltipBodyBuilder.LineTone.Positive);
			}

			return root;
		}

		VisualElement BuildMonthlyEffectList(TooltipContext ctx, string headerText, ResourceStateEntry resource, ResourceDefinition? resDef, bool positiveOnly) {
			var root = TooltipBodyBuilder.NewRoot();
			TooltipBodyBuilder.AddHeader(root, headerText);

			foreach (var effect in resource.Effects) {
				if (effect.PayType != PayType.Monthly) {
					continue;
				}
				bool isPositive = effect.Value > 0;
				if (isPositive != positiveOnly) {
					continue;
				}

				if (effect.EffectId == "base_income" && effect.BaseIncomeBreakdown != null) {
					AddBaseIncomeBreakdown(root, effect);
					continue;
				}

				var effectDef = resDef?.FindEffect(effect.EffectId);
				string effectNameKey = ResourceDisplayNaming.EffectNameKey(effect.EffectId);
				string effectName = effectDef != null ? _loc.TryGet(effectNameKey, effect.EffectId) : effect.EffectId;
				string sign = effect.Value >= 0 ? "+" : "";
				TooltipBodyBuilder.LineTone tone = effect.Value > 0
					? TooltipBodyBuilder.LineTone.Positive
					: effect.Value < 0 ? TooltipBodyBuilder.LineTone.Negative : TooltipBodyBuilder.LineTone.Neutral;
				string effectDescriptionKey = ResourceDisplayNaming.EffectDescriptionKey(effect.EffectId);
				string? description = effectDef != null ? _loc.TryGet(effectDescriptionKey, null) : null;
				TooltipBodyBuilder.AddEffectRow(root, $"{effectName}: {sign}{effect.Value:F1}/month", description, tone);
			}

			return root;
		}

		void AddBaseIncomeBreakdown(VisualElement root, EffectStateEntry effect) {
			var breakdown = effect.BaseIncomeBreakdown!;
			TooltipBodyBuilder.AddLine(root, string.Format(
				_loc.Get("hud.gold_income_base"),
				breakdown.FlatBase.ToString("F1")), TooltipBodyBuilder.LineTone.Positive);
			TooltipBodyBuilder.AddLine(root, string.Format(
				_loc.Get("hud.gold_income_population"),
				FormatResourceValue(breakdown.Population),
				breakdown.PopulationContribution.ToString("F1")), TooltipBodyBuilder.LineTone.Positive);
			TooltipBodyBuilder.AddLine(root, string.Format(
				_loc.Get("hud.gold_income_provinces"),
				breakdown.ProvinceCount.ToString(CultureInfo.InvariantCulture),
				breakdown.ProvinceContribution.ToString("F1")), TooltipBodyBuilder.LineTone.Positive);
			TooltipBodyBuilder.AddLine(root, string.Format(
				_loc.Get("hud.gold_income_economic_advisor"),
				breakdown.AdvisorSkill.ToString("F0"),
				breakdown.AdvisorContribution.ToString("F1")), TooltipBodyBuilder.LineTone.Positive);
		}

		VisualElement BuildInstantEffectList(TooltipContext ctx, string headerText, ResourceStateEntry resource, ResourceDefinition? resDef) {
			var root = TooltipBodyBuilder.NewRoot();
			TooltipBodyBuilder.AddHeader(root, headerText);

			foreach (var effect in resource.Effects) {
				if (effect.PayType == PayType.Monthly) {
					continue;
				}

				var effectDef = resDef?.FindEffect(effect.EffectId);
				string effectNameKey = ResourceDisplayNaming.EffectNameKey(effect.EffectId);
				string effectName = effectDef != null ? _loc.TryGet(effectNameKey, effect.EffectId) : effect.EffectId;
				string sign = effect.Value >= 0 ? "+" : "";
				TooltipBodyBuilder.LineTone tone = effect.Value > 0
					? TooltipBodyBuilder.LineTone.Positive
					: effect.Value < 0 ? TooltipBodyBuilder.LineTone.Negative : TooltipBodyBuilder.LineTone.Neutral;
				string effectDescriptionKey = ResourceDisplayNaming.EffectDescriptionKey(effect.EffectId);
				string? description = effectDef != null ? _loc.TryGet(effectDescriptionKey, null) : null;
				TooltipBodyBuilder.AddEffectRow(root, $"{effectName}: {sign}{effect.Value:F1} instant", description, tone);
			}

			return root;
		}
	}
}
