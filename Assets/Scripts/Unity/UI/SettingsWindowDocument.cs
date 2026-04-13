using System.ComponentModel;
using GS.Game.Commands;
using GS.Game.Components;
using GS.Main;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class SettingsWindowDocument : MonoBehaviour {
		IWriteOnlyCommandAccessor _commands;
		VisualState _visualState;
		ILocalization _loc;
		UIDocument _doc;
		VisualElement _root;

		Label _lblLanguage;
		Label _lblAutoSave;
		Button _btnLangEn;
		Button _btnLangRu;
		Button _btnSaveDaily;
		Button _btnSaveMonthly;
		Button _btnSaveYearly;
		Button _btnBack;

		string _currentLocale = "en";
		AutoSaveInterval _currentInterval = AutoSaveInterval.Monthly;

		[Inject]
		void Construct(IWriteOnlyCommandAccessor commands, VisualState visualState, ILocalization loc) {
			_commands = commands;
			_visualState = visualState;
			_loc = loc;
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
			_btnLangEn = _root.Q<Button>("btn-lang-en");
			_btnLangRu = _root.Q<Button>("btn-lang-ru");
			_btnSaveDaily = _root.Q<Button>("btn-save-daily");
			_btnSaveMonthly = _root.Q<Button>("btn-save-monthly");
			_btnSaveYearly = _root.Q<Button>("btn-save-yearly");
			_btnBack = _root.Q<Button>("btn-back");

			_btnLangEn.clicked += () => SetLocale("en");
			_btnLangRu.clicked += () => SetLocale("ru");
			_btnSaveDaily.clicked += () => SetAutoSave(AutoSaveInterval.Daily);
			_btnSaveMonthly.clicked += () => SetAutoSave(AutoSaveInterval.Monthly);
			_btnSaveYearly.clicked += () => SetAutoSave(AutoSaveInterval.Yearly);
			_btnBack.clicked += Hide;

			Hide();
		}

		public void Show() {
			if (_visualState != null) {
				_currentLocale = _visualState.Locale.Locale;
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
			_btnSaveDaily.text = _loc.Get("settings.save_daily");
			_btnSaveMonthly.text = _loc.Get("settings.save_monthly");
			_btnSaveYearly.text = _loc.Get("settings.save_yearly");
			_btnBack.text = _loc.Get("settings.back");
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

		void RefreshButtons() {
			SetActive(_btnLangEn, _currentLocale == "en");
			SetActive(_btnLangRu, _currentLocale == "ru");
			SetActive(_btnSaveDaily, _currentInterval == AutoSaveInterval.Daily);
			SetActive(_btnSaveMonthly, _currentInterval == AutoSaveInterval.Monthly);
			SetActive(_btnSaveYearly, _currentInterval == AutoSaveInterval.Yearly);
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
