using GS.Game.Components;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Local UI state driving SettingsWindowView.Refresh — mirrors the three local fields
	/// SettingsWindowDocument used to hold directly (_currentLocale/_currentInterval/_tutorialsEnabled).
	/// Not VisualState-backed: settings are config/local UI state, per the plan's note that some of
	/// the six view-less documents are driven by local state rather than VisualState substates.
	/// </summary>
	public struct SettingsWindowViewState {
		public string CurrentLocale;
		public AutoSaveInterval CurrentInterval;
		public bool TutorialsEnabled;
	}

	/// <summary>
	/// Plain view for SettingsWindowDocument (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the six
	/// view-less documents" batch). Owns text refresh and the toggle-button active-state styling;
	/// the document keeps DI, command pushing, and OnClick wiring via the exposed buttons.
	/// </summary>
	public class SettingsWindowView {
		readonly Label _lblLanguage;
		readonly Label _lblAutoSave;
		readonly Label _lblTutorials;
		readonly Label _lblData;
		readonly Label _title;
		readonly Button _btnLangEn;
		readonly Button _btnLangRu;
		readonly Button _btnSaveDaily;
		readonly Button _btnSaveMonthly;
		readonly Button _btnSaveYearly;
		readonly Button _btnTutorialsOn;
		readonly Button _btnTutorialsOff;
		readonly Button _btnDeleteSaves;
		readonly Button _btnResetTutorials;
		readonly Button _btnResetDefaults;
		readonly Button _btnBack;

		public SettingsWindowView(VisualElement root) {
			_title = root.Q<Label>("settings-title");
			_lblLanguage = root.Q<Label>("lbl-language");
			_lblAutoSave = root.Q<Label>("lbl-autosave");
			_lblTutorials = root.Q<Label>("lbl-tutorials");
			_lblData = root.Q<Label>("lbl-data");
			_btnLangEn = root.Q<Button>("btn-lang-en");
			_btnLangRu = root.Q<Button>("btn-lang-ru");
			_btnSaveDaily = root.Q<Button>("btn-save-daily");
			_btnSaveMonthly = root.Q<Button>("btn-save-monthly");
			_btnSaveYearly = root.Q<Button>("btn-save-yearly");
			_btnTutorialsOn = root.Q<Button>("btn-tutorials-on");
			_btnTutorialsOff = root.Q<Button>("btn-tutorials-off");
			_btnDeleteSaves = root.Q<Button>("btn-delete-saves");
			_btnResetTutorials = root.Q<Button>("btn-reset-tutorials");
			_btnResetDefaults = root.Q<Button>("btn-reset-defaults");
			_btnBack = root.Q<Button>("btn-back");
		}

		public Button BtnLangEn => _btnLangEn;
		public Button BtnLangRu => _btnLangRu;
		public Button BtnSaveDaily => _btnSaveDaily;
		public Button BtnSaveMonthly => _btnSaveMonthly;
		public Button BtnSaveYearly => _btnSaveYearly;
		public Button BtnTutorialsOn => _btnTutorialsOn;
		public Button BtnTutorialsOff => _btnTutorialsOff;
		public Button BtnDeleteSaves => _btnDeleteSaves;
		public Button BtnResetTutorials => _btnResetTutorials;
		public Button BtnResetDefaults => _btnResetDefaults;
		public Button BtnBack => _btnBack;

		public void RefreshTexts(ILocalization loc) {
			if (_lblLanguage == null) {
				return;
			}
			if (_title != null) {
				_title.text = loc.Get("settings.title");
			}
			_lblLanguage.text = loc.Get("settings.language");
			_lblAutoSave.text = loc.Get("settings.autosave");
			if (_lblTutorials != null) {
				_lblTutorials.text = loc.Get("settings.tutorials");
			}
			_btnSaveDaily.text = loc.Get("settings.save_daily");
			_btnSaveMonthly.text = loc.Get("settings.save_monthly");
			_btnSaveYearly.text = loc.Get("settings.save_yearly");
			_lblData.text = loc.Get("settings.data");
			_btnDeleteSaves.text = loc.Get("settings.delete_saves");
			if (_btnResetTutorials != null) {
				_btnResetTutorials.text = loc.Get("settings.reset_tutorials");
			}
			if (_btnResetDefaults != null) {
				_btnResetDefaults.text = loc.Get("settings.reset_defaults");
			}
			_btnBack.text = loc.Get("settings.back");
		}

		public void Refresh(SettingsWindowViewState state) {
			if (_btnLangEn == null) {
				return;
			}
			SetActive(_btnLangEn, state.CurrentLocale == "en");
			SetActive(_btnLangRu, state.CurrentLocale == "ru");
			SetActive(_btnSaveDaily, state.CurrentInterval == AutoSaveInterval.Daily);
			SetActive(_btnSaveMonthly, state.CurrentInterval == AutoSaveInterval.Monthly);
			SetActive(_btnSaveYearly, state.CurrentInterval == AutoSaveInterval.Yearly);
			if (_btnTutorialsOn != null) {
				SetActive(_btnTutorialsOn, state.TutorialsEnabled);
			}
			if (_btnTutorialsOff != null) {
				SetActive(_btnTutorialsOff, !state.TutorialsEnabled);
			}
		}

		static void SetActive(Button btn, bool active) {
			if (active) {
				btn.AddToClassList("gs-toggle-on");
			} else {
				btn.RemoveFromClassList("gs-toggle-on");
			}
		}
	}
}
