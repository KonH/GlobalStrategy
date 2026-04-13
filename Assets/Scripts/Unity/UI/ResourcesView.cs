using UnityEngine.UIElements;
using GS.Main;
using GS.Game.Components;
using GS.Game.Configs;

namespace GS.Unity.UI {
	class ResourcesView {
		readonly VisualElement _container;
		readonly ILocalization _loc;
		readonly ResourceConfig _config;
		readonly TooltipController _tooltip;

		public ResourcesView(VisualElement container, ILocalization loc, ResourceConfig config, TooltipController tooltip) {
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
				var label = new Label();
				label.AddToClassList("gs-label");
				label.AddToClassList("resource-label");

				double netMonthly = 0;
				foreach (var effect in resource.Effects) {
					if (effect.PayType == PayType.Monthly) {
						netMonthly += effect.Value;
					}
				}

				var resDef = _config.FindResource(resource.ResourceId);
				string icon = resDef?.Icon ?? "*";
				label.text = $"{icon} {resource.Value:F0}";

				label.RemoveFromClassList("gs-color-positive");
				label.RemoveFromClassList("gs-color-negative");
				if (netMonthly > 0) {
					label.AddToClassList("gs-color-positive");
				} else if (netMonthly < 0) {
					label.AddToClassList("gs-color-negative");
				}

				var capturedResource = resource;
				_tooltip.RegisterTooltip(label, () => BuildResourceTooltip(capturedResource));

				_container.Add(label);
			}
		}

		VisualElement BuildResourceTooltip(ResourceStateEntry resource) {
			var root = new VisualElement();

			var header = new Label();
			var resDef = _config.FindResource(resource.ResourceId);
			header.text = resDef != null ? _loc.Get(resDef.NameKey) : resource.ResourceId;
			header.AddToClassList("tooltip-header");
			root.Add(header);

			foreach (var effect in resource.Effects) {
				var effectRow = new VisualElement();
				effectRow.AddToClassList("tooltip-effect-row");

				var effectDef = resDef?.FindEffect(effect.EffectId);

				string effectName = effectDef != null ? _loc.Get(effectDef.NameKey) : effect.EffectId;
				string sign = effect.Value >= 0 ? "+" : "";
				string payLabel = effect.PayType == PayType.Monthly ? "/month" : " (instant)";

				var nameLabel = new Label($"{effectName}: {sign}{effect.Value:F1}{payLabel}");
				nameLabel.AddToClassList("tooltip-effect-name");
				if (effect.Value > 0) {
					nameLabel.AddToClassList("tooltip-effect-positive");
				} else if (effect.Value < 0) {
					nameLabel.AddToClassList("tooltip-effect-negative");
				}
				effectRow.Add(nameLabel);

				// Description shown on hover over effect row
				if (effectDef != null) {
					var descLabel = new Label(_loc.Get(effectDef.DescriptionKey));
					descLabel.AddToClassList("tooltip-description");
					descLabel.style.display = DisplayStyle.None;
					effectRow.Add(descLabel);

					effectRow.RegisterCallback<PointerEnterEvent>(_ => {
						descLabel.style.display = DisplayStyle.Flex;
					});
					effectRow.RegisterCallback<PointerLeaveEvent>(_ => {
						descLabel.style.display = DisplayStyle.None;
					});
				}

				root.Add(effectRow);
			}

			return root;
		}
	}
}
