using System.ComponentModel;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Save;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class SettingsWindowDocument : MonoBehaviour {
		IWriteOnlyCommandAccessor _commands;
		VisualState _visualState;
		ILocalization _loc;
		SaveFileManager _saveFileManager;
		IFlyTextNotifier _flyText;
		SettingsStorage _settings;
		GameSettings _gameSettings;
		UIDocument _doc;
		VisualElement _root;

		Label _lblLanguage;
		Label _lblAutoSave;
		Label _lblTutorials;
		Label _lblData;
		Button _btnLangEn;
		Button _btnLangRu;
		Button _btnSaveDaily;
		Button _btnSaveMonthly;
		Button _btnSaveYearly;
		Button _btnTutorialsOn;
		Button _btnTutorialsOff;
		Button _btnDeleteSaves;
		Button _btnResetTutorials;
		Button _btnResetDefaults;
		Button _btnBack;

		string _currentLocale = "en";
		AutoSaveInterval _currentInterval = AutoSaveInterval.Monthly;
		bool _tutorialsEnabled = true;

		[Inject]
		void Construct(
				IWriteOnlyCommandAccessor commands,
				VisualState visualState,
				ILocalization loc,
				SaveFileManager saveFileManager,
				IFlyTextNotifier flyText,
				SettingsStorage settings,
				GameSettings gameSettings) {
			_commands = commands;
			_visualState = visualState;
			_loc = loc;
			_saveFileManager = saveFileManager;
			_flyText = flyText;
			_settings = settings;
			_gameSettings = gameSettings;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void OnEnable() {
			if (_visualState != null) {
				_visualState.Locale.PropertyChanged += HandleLocaleChanged;
			}
		}

		void OnDisable() {
			if (_visualState != null) {
				_visualState.Locale.PropertyChanged -= HandleLocaleChanged;
			}
		}

		void Start() {
			_root = _doc.rootVisualElement;
			_lblLanguage = _root.Q<Label>("lbl-language");
			_lblAutoSave = _root.Q<Label>("lbl-autosave");
			_lblTutorials = _root.Q<Label>("lbl-tutorials");
			_lblData = _root.Q<Label>("lbl-data");
			_btnLangEn = _root.Q<Button>("btn-lang-en");
			_btnLangRu = _root.Q<Button>("btn-lang-ru");
			_btnSaveDaily = _root.Q<Button>("btn-save-daily");
			_btnSaveMonthly = _root.Q<Button>("btn-save-monthly");
			_btnSaveYearly = _root.Q<Button>("btn-save-yearly");
			_btnTutorialsOn = _root.Q<Button>("btn-tutorials-on");
			_btnTutorialsOff = _root.Q<Button>("btn-tutorials-off");
			_btnDeleteSaves = _root.Q<Button>("btn-delete-saves");
			_btnResetTutorials = _root.Q<Button>("btn-reset-tutorials");
			_btnResetDefaults = _root.Q<Button>("btn-reset-defaults");
			_btnBack = _root.Q<Button>("btn-back");

			_btnLangEn.clicked += () => SetLocale("en");
			_btnLangRu.clicked += () => SetLocale("ru");
			_btnSaveDaily.clicked += () => SetAutoSave(AutoSaveInterval.Daily);
			_btnSaveMonthly.clicked += () => SetAutoSave(AutoSaveInterval.Monthly);
			_btnSaveYearly.clicked += () => SetAutoSave(AutoSaveInterval.Yearly);
			_btnTutorialsOn.clicked += () => SetTutorialsEnabled(true);
			_btnTutorialsOff.clicked += () => SetTutorialsEnabled(false);
			_btnDeleteSaves.clicked += DeleteAllSaves;
			_btnResetTutorials.clicked += ResetTutorials;
			_btnResetDefaults.clicked += ResetDefaults;
			_btnBack.clicked += Hide;

			Hide();
		}

		public void Show() {
			if (_visualState != null) {
				_currentLocale = _visualState.Locale.Locale;
			}
			if (_settings != null) {
				_tutorialsEnabled = _settings.TutorialsEnabled;
			}
			RefreshTexts();
			RefreshButtons();
			_root.style.display = DisplayStyle.Flex;
		}

		public void Hide() {
			_root.style.display = DisplayStyle.None;
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
			RefreshButtons();
		}

		void RefreshTexts() {
			if (_lblLanguage == null) {
				return;
			}
			var lblTitle = _root.Q<Label>("settings-title");
			if (lblTitle != null) {
				lblTitle.text = _loc.Get("settings.title");
			}
			_lblLanguage.text = _loc.Get("settings.language");
			_lblAutoSave.text = _loc.Get("settings.autosave");
			if (_lblTutorials != null) {
				_lblTutorials.text = _loc.Get("settings.tutorials");
			}
			_btnSaveDaily.text = _loc.Get("settings.save_daily");
			_btnSaveMonthly.text = _loc.Get("settings.save_monthly");
			_btnSaveYearly.text = _loc.Get("settings.save_yearly");
			_lblData.text = _loc.Get("settings.data");
			_btnDeleteSaves.text = _loc.Get("settings.delete_saves");
			if (_btnResetTutorials != null) {
				_btnResetTutorials.text = _loc.Get("settings.reset_tutorials");
			}
			if (_btnResetDefaults != null) {
				_btnResetDefaults.text = _loc.Get("settings.reset_defaults");
			}
			_btnBack.text = _loc.Get("settings.back");
		}

		void DeleteAllSaves() {
			_saveFileManager?.DeleteAllSaves();
			_flyText?.Notify("settings.delete_saves.confirmation");
		}

		void SetLocale(string locale) {
			_currentLocale = locale;
			_commands?.Push(new ChangeLocaleCommand(locale));
			RefreshButtons();
		}

		void SetAutoSave(AutoSaveInterval interval) {
			_currentInterval = interval;
			string intervalStr = interval switch {
				AutoSaveInterval.Daily => "daily",
				AutoSaveInterval.Yearly => "yearly",
				_ => "monthly"
			};
			_commands?.Push(new ChangeAutoSaveIntervalCommand(intervalStr));
			RefreshButtons();
		}

		void SetTutorialsEnabled(bool enabled) {
			_tutorialsEnabled = enabled;
			if (_settings != null) {
				_settings.TutorialsEnabled = enabled;
			}
			_commands?.Push(new SetTutorialsEnabledCommand(enabled));
			RefreshButtons();
		}

		void ResetTutorials() {
			_settings?.ClearCompletedTutorials();
		}

		void ResetDefaults() {
			_settings?.ResetToDefaults();
			_tutorialsEnabled = _settings == null || _settings.TutorialsEnabled;
			_currentLocale = string.IsNullOrEmpty(_settings?.Locale) ? "en" : _settings.Locale;
			string defaultAutoSave = _gameSettings != null && !string.IsNullOrEmpty(_gameSettings.AutoSaveInterval)
				? _gameSettings.AutoSaveInterval
				: "monthly";
			_currentInterval = defaultAutoSave switch {
				"daily" => AutoSaveInterval.Daily,
				"yearly" => AutoSaveInterval.Yearly,
				_ => AutoSaveInterval.Monthly
			};

			_loc?.SetLocale(_currentLocale);
			_commands?.Push(new ChangeLocaleCommand(_currentLocale));
			_commands?.Push(new ChangeAutoSaveIntervalCommand(defaultAutoSave));
			_commands?.Push(new SetTutorialsEnabledCommand(_tutorialsEnabled));

			RefreshTexts();
			RefreshButtons();
		}

		void RefreshButtons() {
			if (_btnLangEn == null) {
				return;
			}
			SetActive(_btnLangEn, _currentLocale == "en");
			SetActive(_btnLangRu, _currentLocale == "ru");
			SetActive(_btnSaveDaily, _currentInterval == AutoSaveInterval.Daily);
			SetActive(_btnSaveMonthly, _currentInterval == AutoSaveInterval.Monthly);
			SetActive(_btnSaveYearly, _currentInterval == AutoSaveInterval.Yearly);
			if (_btnTutorialsOn != null) {
				SetActive(_btnTutorialsOn, _tutorialsEnabled);
			}
			if (_btnTutorialsOff != null) {
				SetActive(_btnTutorialsOff, !_tutorialsEnabled);
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
