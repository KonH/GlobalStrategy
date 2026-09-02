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
		SettingsWindowView _view;

		SettingsWindowViewState _viewState = new SettingsWindowViewState {
			CurrentLocale = "en",
			CurrentInterval = AutoSaveInterval.Monthly,
			TutorialsEnabled = true
		};

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
			_view = new SettingsWindowView(_root);

			_view.BtnLangEn.OnClick(() => SetLocale("en"));
			_view.BtnLangRu.OnClick(() => SetLocale("ru"));
			_view.BtnSaveDaily.OnClick(() => SetAutoSave(AutoSaveInterval.Daily));
			_view.BtnSaveMonthly.OnClick(() => SetAutoSave(AutoSaveInterval.Monthly));
			_view.BtnSaveYearly.OnClick(() => SetAutoSave(AutoSaveInterval.Yearly));
			_view.BtnTutorialsOn.OnClick(() => SetTutorialsEnabled(true));
			_view.BtnTutorialsOff.OnClick(() => SetTutorialsEnabled(false));
			_view.BtnDeleteSaves.OnClick(DeleteAllSaves);
			_view.BtnResetTutorials.OnClick(ResetTutorials);
			_view.BtnResetDefaults.OnClick(ResetDefaults);
			_view.BtnBack.OnClick(Hide);

			Hide();
		}

		public void Show() {
			if (_visualState != null) {
				_viewState.CurrentLocale = _visualState.Locale.Locale;
			}
			if (_settings != null) {
				_viewState.TutorialsEnabled = _settings.TutorialsEnabled;
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
			_view?.RefreshTexts(_loc);
		}

		void DeleteAllSaves() {
			_saveFileManager?.DeleteAllSaves();
			_flyText?.Notify("settings.delete_saves.confirmation");
		}

		void SetLocale(string locale) {
			_viewState.CurrentLocale = locale;
			_commands?.Push(new ChangeLocaleCommand(locale));
			RefreshButtons();
		}

		void SetAutoSave(AutoSaveInterval interval) {
			_viewState.CurrentInterval = interval;
			string intervalStr = interval switch {
				AutoSaveInterval.Daily => "daily",
				AutoSaveInterval.Yearly => "yearly",
				_ => "monthly"
			};
			_commands?.Push(new ChangeAutoSaveIntervalCommand(intervalStr));
			RefreshButtons();
		}

		void SetTutorialsEnabled(bool enabled) {
			_viewState.TutorialsEnabled = enabled;
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
			_viewState.TutorialsEnabled = _settings == null || _settings.TutorialsEnabled;
			_viewState.CurrentLocale = string.IsNullOrEmpty(_settings?.Locale) ? "en" : _settings.Locale;
			string defaultAutoSave = _gameSettings != null && !string.IsNullOrEmpty(_gameSettings.AutoSaveInterval)
				? _gameSettings.AutoSaveInterval
				: "monthly";
			_viewState.CurrentInterval = defaultAutoSave switch {
				"daily" => AutoSaveInterval.Daily,
				"yearly" => AutoSaveInterval.Yearly,
				_ => AutoSaveInterval.Monthly
			};

			_loc?.SetLocale(_viewState.CurrentLocale);
			_commands?.Push(new ChangeLocaleCommand(_viewState.CurrentLocale));
			_commands?.Push(new ChangeAutoSaveIntervalCommand(defaultAutoSave));
			_commands?.Push(new SetTutorialsEnabledCommand(_viewState.TutorialsEnabled));

			RefreshTexts();
			RefreshButtons();
		}

		void RefreshButtons() {
			_view?.Refresh(_viewState);
		}
	}
}
