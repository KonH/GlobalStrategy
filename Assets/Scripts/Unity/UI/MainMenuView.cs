using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Plain view for MainMenuDocument (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the six
	/// view-less documents" batch). Queries the static menu elements and exposes the buttons so the
	/// document can wire OnClick/DI; owns only "given data, set VisualElement content".
	/// </summary>
	public class MainMenuView {
		readonly Label _titleLabel;
		readonly Button _btnPlay;
		readonly Button _btnResume;
		readonly Button _btnLoad;
		readonly Button _btnSettings;
		readonly Button _btnAbout;
		readonly Button _btnExit;
		readonly Label _versionNameLabel;
		readonly Label _versionNumberLabel;

		public MainMenuView(VisualElement root) {
			_titleLabel = root.Q<Label>("title-label");
			_btnPlay = root.Q<Button>("btn-play");
			_btnResume = root.Q<Button>("btn-resume");
			_btnLoad = root.Q<Button>("btn-load");
			_btnSettings = root.Q<Button>("btn-settings");
			_btnAbout = root.Q<Button>("btn-about");
			_btnExit = root.Q<Button>("btn-exit");
			_versionNameLabel = root.Q<Label>("version-name");
			_versionNumberLabel = root.Q<Label>("version-label");
		}

		public Button BtnPlay => _btnPlay;
		public Button BtnResume => _btnResume;
		public Button BtnLoad => _btnLoad;
		public Button BtnSettings => _btnSettings;
		public Button BtnAbout => _btnAbout;
		public Button BtnExit => _btnExit;

		public void SetVersion(string versionName, string versionNumberText) {
			if (_versionNameLabel != null) {
				_versionNameLabel.text = versionName;
			}
			if (_versionNumberLabel != null) {
				_versionNumberLabel.text = versionNumberText;
			}
		}

		public void RefreshTexts(ILocalization loc) {
			if (_btnPlay == null) {
				return;
			}
			_titleLabel.text = loc.Get("menu.title");
			_btnPlay.text = loc.Get("menu.play");
			_btnResume.text = loc.Get("menu.resume");
			_btnLoad.text = loc.Get("menu.load");
			_btnSettings.text = loc.Get("menu.settings");
			_btnAbout.text = loc.Get("menu.about");
			_btnExit.text = loc.Get("menu.exit");
		}

		public void Refresh(bool hasSaves) {
			_btnResume.style.display = hasSaves ? DisplayStyle.Flex : DisplayStyle.None;
			_btnLoad.style.display = hasSaves ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
