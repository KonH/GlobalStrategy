using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using GS.Game.Commands;
using GS.Game.Common;

namespace GS.Unity.UI {
	class LensSwitcherView {
		readonly VisualElement _root;
		readonly Button _currentBtn;
		readonly VisualElement _currentIcon;
		readonly VisualElement _expandPanel;
		readonly Button _btnPolitical;
		readonly Button _btnGeographic;
		readonly Button _btnOrg;
		readonly Button _btnProvince;
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
			_btnProvince = root.Q<Button>("lens-btn-province");

			_currentBtn.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _currentBtn.ContainsPoint(e.localPosition)) ToggleExpand(); });
			_btnPolitical.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnPolitical.ContainsPoint(e.localPosition)) SelectLens(MapLens.Political); });
			_btnGeographic.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnGeographic.ContainsPoint(e.localPosition)) SelectLens(MapLens.Geographic); });
			_btnOrg.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnOrg.ContainsPoint(e.localPosition)) SelectLens(MapLens.Org); });
			_btnProvince.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnProvince.ContainsPoint(e.localPosition)) SelectLens(MapLens.Province); });

			if (tooltip != null && loc != null) {
				RegisterTooltip(tooltip, loc, _btnPolitical, "lens-political", "hud.lens.political");
				RegisterTooltip(tooltip, loc, _btnGeographic, "lens-geographic", "hud.lens.geographic");
				RegisterTooltip(tooltip, loc, _btnOrg, "lens-org", "hud.lens.org");
				RegisterTooltip(tooltip, loc, _btnProvince, "lens-province", "hud.lens.province");
			}
		}

		public void Refresh(MapLens activeLens) {
			foreach (var cls in new[] { "lens-icon--political", "lens-icon--geographic", "lens-icon--org", "lens-icon--province" }) {
				_currentIcon.RemoveFromClassList(cls);
			}
			_currentIcon.AddToClassList(GetIconClass(activeLens));

			SetActive(_btnPolitical, activeLens == MapLens.Political);
			SetActive(_btnGeographic, activeLens == MapLens.Geographic);
			SetActive(_btnOrg, activeLens == MapLens.Org);
			SetActive(_btnProvince, activeLens == MapLens.Province);
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
				case MapLens.Province: return "lens-icon--province";
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
