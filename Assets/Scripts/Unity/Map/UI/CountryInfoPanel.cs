using TMPro;
using UnityEngine;

namespace GS.Unity.Map.UI {
	public class CountryInfoPanel : MonoBehaviour {
		[SerializeField] TMP_Text _countryNameText;

		public void Present(string name) {
			gameObject.SetActive(true);
			_countryNameText.text = name;
		}

		public void Hide() {
			gameObject.SetActive(false);
		}
	}
}
