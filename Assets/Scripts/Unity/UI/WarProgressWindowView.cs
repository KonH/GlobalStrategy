using System;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Map;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	public class WarProgressWindowView {
		readonly WarProgressLayoutBinder _binder;

		public WarProgressWindowView(
			VisualElement root, ILocalization loc, CountryVisualConfig countryVisualConfig,
			EffectConfig effectConfig, TooltipSystem tooltip) {
			_binder = new WarProgressLayoutBinder(root, loc, countryVisualConfig, effectConfig, tooltip);
		}

		public void RefreshStaticTexts(Func<string, string, string> getText) {
			_binder.RefreshStaticTexts(getText);
		}

		public void Refresh(SelectedWarState state) {
			if (state == null || !state.IsValid) {
				return;
			}
			_binder.Refresh(
				state.Attacker.CountryId,
				state.Defender.CountryId,
				state.Progress,
				state.History,
				state.Attacker,
				state.Defender,
				state.Battles);
		}
	}
}
