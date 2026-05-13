using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Game.Commands;

namespace GS.Unity.UI {
	class LensSwitcherView {
		readonly VisualElement _root;
		readonly Button _currentBtn;
		readonly VisualElement _currentIcon;
		readonly VisualElement _expandPanel;
		readonly Button _btnPolitical;
		readonly Button _btnGeographic;
		readonly Button _btnOrg;
		bool _isExpanded;

		public Action<MapLens> OnLensSelected;

		public LensSwitcherView(VisualElement root, TooltipSystem tooltip, ILocalization loc) {
			_root = root;
			_currentBtn = root.Q<Button>("lens-current-btn");
			_currentIcon = root.Q("lens-current-icon");
			_expandPanel = root.Q("lens-expand-panel");
			_btnPolitical = root.Q<Button>("lens-btn-political");
			_btnGeographic = root.Q<Button>("lens-btn-geographic");
			_btnOrg = root.Q<Button>("lens-btn-org");

			_currentBtn.clicked += ToggleExpand;
			_btnPolitical.clicked += () => SelectLens(MapLens.Political);
			_btnGeographic.clicked += () => SelectLens(MapLens.Geographic);
			_btnOrg.clicked += () => SelectLens(MapLens.Org);

			if (tooltip != null && loc != null) {
				RegisterTooltip(tooltip, loc, _btnPolitical, "lens-political", "hud.lens.political");
				RegisterTooltip(tooltip, loc, _btnGeographic, "lens-geographic", "hud.lens.geographic");
				RegisterTooltip(tooltip, loc, _btnOrg, "lens-org", "hud.lens.org");
			}
		}

		public void Refresh(MapLens activeLens) {
			foreach (var cls in new[] { "lens-icon--political", "lens-icon--geographic", "lens-icon--org" }) {
				_currentIcon.RemoveFromClassList(cls);
			}
			_currentIcon.AddToClassList(GetIconClass(activeLens));

			SetActive(_btnPolitical, activeLens == MapLens.Political);
			SetActive(_btnGeographic, activeLens == MapLens.Geographic);
			SetActive(_btnOrg, activeLens == MapLens.Org);
		}

		void ToggleExpand() {
			_isExpanded = !_isExpanded;
			_expandPanel.style.display = _isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void SelectLens(MapLens lens) {
			OnLensSelected?.Invoke(lens);
			_isExpanded = false;
			_expandPanel.style.display = DisplayStyle.None;
		}

		void SetActive(Button btn, bool active) {
			if (active) {
				btn.AddToClassList("lens-btn--active");
			} else {
				btn.RemoveFromClassList("lens-btn--active");
			}
		}

		static string GetIconClass(MapLens lens) {
			switch (lens) {
				case MapLens.Geographic: return "lens-icon--geographic";
				case MapLens.Org: return "lens-icon--org";
				default: return "lens-icon--political";
			}
		}

		static void RegisterTooltip(TooltipSystem tooltip, ILocalization loc, VisualElement trigger, string id, string locKey) {
			tooltip.RegisterTrigger(trigger, id, _ => {
				var label = new Label(loc.Get(locKey));
				label.AddToClassList("gs-content");
				return label;
			}, new HashSet<string>());
		}
	}
}
