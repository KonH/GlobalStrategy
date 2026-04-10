using System;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	class TooltipController {
		readonly VisualElement _panel;
		bool _isOverTrigger;
		bool _isOverPanel;

		public TooltipController(VisualElement panel) {
			_panel = panel;
			_panel.style.display = DisplayStyle.None;
			_panel.RegisterCallback<PointerEnterEvent>(_ => {
				_isOverPanel = true;
			});
			_panel.RegisterCallback<PointerLeaveEvent>(_ => {
				_isOverPanel = false;
				TryHide();
			});
		}

		public void RegisterTooltip(VisualElement trigger, Func<VisualElement> buildContent) {
			trigger.RegisterCallback<PointerEnterEvent>(_ => {
				_isOverTrigger = true;
				ShowTooltip(trigger, buildContent);
			});
			trigger.RegisterCallback<PointerLeaveEvent>(_ => {
				_isOverTrigger = false;
				TryHide();
			});
		}

		void ShowTooltip(VisualElement trigger, Func<VisualElement> buildContent) {
			_panel.Clear();
			_panel.Add(buildContent());
			_panel.style.display = DisplayStyle.Flex;
			PositionNear(trigger);
		}

		void TryHide() {
			if (!_isOverTrigger && !_isOverPanel) {
				_panel.style.display = DisplayStyle.None;
			}
		}

		void PositionNear(VisualElement trigger) {
			var bound = trigger.worldBound;
			_panel.style.left = bound.xMin;
			_panel.style.top = bound.yMax + 4;
		}
	}
}
