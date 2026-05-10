using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Unity.UI {
	class ResourcesView {
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
			foreach (var resource in state.Resources) {
				var row = new VisualElement();
				row.AddToClassList("resource-row");

				var icon = new VisualElement();
				icon.AddToClassList($"resource-icon--{resource.ResourceId}");
				row.Add(icon);

				var label = new Label();
				label.AddToClassList("gs-label");
				label.AddToClassList("resource-label");
				label.text = $"{resource.Value:F0}";
				row.Add(label);

				var capturedResource = resource;
				var capturedState = state;
				_tooltip.RegisterTrigger(row, capturedResource.ResourceId, ctx => BuildResourceTooltip(ctx, capturedResource, capturedState), new HashSet<string>());

				_container.Add(row);
			}
		}

		VisualElement BuildResourceTooltip(TooltipContext ctx, ResourceStateEntry resource, CountryResourcesState state) {
			var root = new VisualElement();

			var resDef = _config.FindResource(resource.ResourceId);

			var header = new Label();
			header.text = resDef != null ? _loc.Get(resDef.NameKey) : resource.ResourceId;
			header.AddToClassList("tooltip-header");
			root.Add(header);

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
				var plusRow = new Label(plusText);
				plusRow.AddToClassList("tooltip-effect-name");
				plusRow.AddToClassList("tooltip-effect-positive");
				plusRow.AddToClassList("tooltip-inner-trigger");
				root.Add(plusRow);

				string capturedText = plusText;
				string capturedId = resource.ResourceId;
				ctx.RegisterInnerTrigger(plusRow, $"{capturedId}.plus", innerCtx =>
					BuildMonthlyEffectList(innerCtx, capturedText, resource, resDef, positiveOnly: true));
			}

			if (minusTotal < 0) {
				string minusText = $"{minusTotal:F1}/month";
				var minusRow = new Label(minusText);
				minusRow.AddToClassList("tooltip-effect-name");
				minusRow.AddToClassList("tooltip-effect-negative");
				minusRow.AddToClassList("tooltip-inner-trigger");
				root.Add(minusRow);

				string capturedText = minusText;
				string capturedId = resource.ResourceId;
				ctx.RegisterInnerTrigger(minusRow, $"{capturedId}.minus", innerCtx =>
					BuildMonthlyEffectList(innerCtx, capturedText, resource, resDef, positiveOnly: false));
			}

			if (instantTotal != 0) {
				string sign = instantTotal > 0 ? "+" : "";
				string instantText = $"{sign}{instantTotal:F1} instant";
				var instantRow = new Label(instantText);
				instantRow.AddToClassList("tooltip-effect-name");
				if (instantTotal > 0) {
					instantRow.AddToClassList("tooltip-effect-positive");
				} else {
					instantRow.AddToClassList("tooltip-effect-negative");
				}
				instantRow.AddToClassList("tooltip-inner-trigger");
				root.Add(instantRow);

				string capturedText = instantText;
				string capturedId = resource.ResourceId;
				ctx.RegisterInnerTrigger(instantRow, $"{capturedId}.instant", innerCtx =>
					BuildInstantEffectList(innerCtx, capturedText, resource, resDef));
			}

			// Influence income rows (gold resource only)
			if (resource.ResourceId == "gold" && state.InfluenceIncomes.Count > 0) {
				double influenceTotal = 0;
				foreach (var inc in state.InfluenceIncomes) {
					influenceTotal += inc.MonthlyGold;
				}
				string influenceText = $"+{influenceTotal:F1}/month";
				var influenceRow = new Label(influenceText);
				influenceRow.AddToClassList("tooltip-effect-name");
				influenceRow.AddToClassList("tooltip-effect-positive");
				influenceRow.AddToClassList("tooltip-inner-trigger");
				root.Add(influenceRow);

				var capturedState = state;
				string capturedText = influenceText;
				ctx.RegisterInnerTrigger(influenceRow, "gold.influence", innerCtx =>
					BuildInfluenceIncomeList(capturedText, capturedState));
			}

			return root;
		}

		VisualElement BuildInfluenceIncomeList(string headerText, CountryResourcesState state) {
			var root = new VisualElement();

			var header = new Label(headerText);
			header.AddToClassList("tooltip-header");
			root.Add(header);

			foreach (var inc in state.InfluenceIncomes) {
				string countryName = _loc.Get($"country_name.{inc.CountryId}");
				var row = new Label($"Influence ({countryName}): +{inc.MonthlyGold:F1}/month");
				row.AddToClassList("tooltip-effect-name");
				row.AddToClassList("tooltip-effect-positive");
				root.Add(row);
			}

			return root;
		}

		VisualElement BuildMonthlyEffectList(TooltipContext ctx, string headerText, ResourceStateEntry resource, ResourceDefinition resDef, bool positiveOnly) {
			var root = new VisualElement();

			var header = new Label(headerText);
			header.AddToClassList("tooltip-header");
			root.Add(header);

			foreach (var effect in resource.Effects) {
				if (effect.PayType != PayType.Monthly) {
					continue;
				}
				bool isPositive = effect.Value > 0;
				if (isPositive != positiveOnly) {
					continue;
				}

				var effectRow = new VisualElement();
				effectRow.AddToClassList("tooltip-effect-row");

				var effectDef = resDef?.FindEffect(effect.EffectId);
				string effectName = effectDef != null ? _loc.Get(effectDef.NameKey) : effect.EffectId;
				string sign = effect.Value >= 0 ? "+" : "";
				var nameLabel = new Label($"{effectName}: {sign}{effect.Value:F1}/month");
				nameLabel.AddToClassList("tooltip-effect-name");
				if (effect.Value > 0) {
					nameLabel.AddToClassList("tooltip-effect-positive");
				} else if (effect.Value < 0) {
					nameLabel.AddToClassList("tooltip-effect-negative");
				}
				effectRow.Add(nameLabel);

				if (effectDef != null) {
					var descLabel = new Label(_loc.Get(effectDef.DescriptionKey));
					descLabel.AddToClassList("tooltip-description");
					effectRow.Add(descLabel);
				}

				root.Add(effectRow);
			}

			return root;
		}

		VisualElement BuildInstantEffectList(TooltipContext ctx, string headerText, ResourceStateEntry resource, ResourceDefinition resDef) {
			var root = new VisualElement();

			var header = new Label(headerText);
			header.AddToClassList("tooltip-header");
			root.Add(header);

			foreach (var effect in resource.Effects) {
				if (effect.PayType == PayType.Monthly) {
					continue;
				}

				var effectRow = new VisualElement();
				effectRow.AddToClassList("tooltip-effect-row");

				var effectDef = resDef?.FindEffect(effect.EffectId);
				string effectName = effectDef != null ? _loc.Get(effectDef.NameKey) : effect.EffectId;
				string sign = effect.Value >= 0 ? "+" : "";
				var nameLabel = new Label($"{effectName}: {sign}{effect.Value:F1} instant");
				nameLabel.AddToClassList("tooltip-effect-name");
				if (effect.Value > 0) {
					nameLabel.AddToClassList("tooltip-effect-positive");
				} else if (effect.Value < 0) {
					nameLabel.AddToClassList("tooltip-effect-negative");
				}
				effectRow.Add(nameLabel);

				if (effectDef != null) {
					var descLabel = new Label(_loc.Get(effectDef.DescriptionKey));
					descLabel.AddToClassList("tooltip-description");
					effectRow.Add(descLabel);
				}

				root.Add(effectRow);
			}

			return root;
		}
	}
}
