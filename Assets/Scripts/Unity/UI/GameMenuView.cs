using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Plain view for GameMenuDocument (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the six
	/// view-less documents" batch). No dynamic display state beyond localized text — the document
	/// still owns Show/Hide (root display), pause/save commands, and OnClick wiring via the exposed
	/// buttons.
	/// </summary>
	public class GameMenuView {
		readonly Label _title;
		readonly Button _btnResume;
		readonly Button _btnSave;
		readonly Button _btnExit;

		public GameMenuView(VisualElement root) {
			_title = root.Q<Label>("menu-title");
			_btnResume = root.Q<Button>("btn-resume");
			_btnSave = root.Q<Button>("btn-save");
			_btnExit = root.Q<Button>("btn-exit");
		}

		public Button BtnResume => _btnResume;
		public Button BtnSave => _btnSave;
		public Button BtnExit => _btnExit;

		public void RefreshTexts(ILocalization loc) {
			if (_title == null) {
				return;
			}
			_title.text = loc.Get("game_menu.title");
			_btnResume.text = loc.Get("game_menu.resume");
			_btnSave.text = loc.Get("game_menu.save");
			_btnExit.text = loc.Get("game_menu.exit");
		}
	}
}
