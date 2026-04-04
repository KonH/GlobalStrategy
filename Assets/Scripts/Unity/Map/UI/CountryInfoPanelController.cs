using System;
using UnityEngine;

namespace GS.Unity.Map.UI {
	public class CountryInfoPanelController : MonoBehaviour {
		[SerializeField] CountryInfoPanel _panel;

		public void Awake() {
			_panel.Hide();
		}

		public void HandleSelectionChanged(CountryEntry entry) {
			if (entry == null) {
				_panel.Hide();
				return;
			}
			_panel.Present(entry.displayName);
		}
	}
}
