using System.ComponentModel;
using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class MainMenuDocument : MonoBehaviour {
		SaveFileManager _saveFileManager;
		SceneLoader _sceneLoader;
		LoadWindowDocument _loadWindow;
		SettingsWindowDocument _settingsWindow;
		VisualState _state;
		ILocalization _loc;
		UIDocument _doc;

		Button _btnPlay;
		Button _btnResume;
		Button _btnLoad;
		Button _btnSettings;
		Button _btnExit;

		[Inject]
		void Construct(SaveFileManager saveFileManager, SceneLoader sceneLoader, LoadWindowDocument loadWindow, SettingsWindowDocument settingsWindow, VisualState state, ILocalization loc) {
			_saveFileManager = saveFileManager;
			_sceneLoader = sceneLoader;
			_loadWindow = loadWindow;
			_settingsWindow = settingsWindow;
			_state = state;
			_loc = loc;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void OnEnable() {
			if (_state != null) {
				_state.Locale.PropertyChanged += HandleLocaleChanged;
			}
		}

		void OnDisable() {
			if (_state != null) {
				_state.Locale.PropertyChanged -= HandleLocaleChanged;
			}
		}

		void Start() {
			var root = _doc.rootVisualElement;

			_btnPlay = root.Q<Button>("btn-play");
			_btnResume = root.Q<Button>("btn-resume");
			_btnLoad = root.Q<Button>("btn-load");
			_btnSettings = root.Q<Button>("btn-settings");
			_btnExit = root.Q<Button>("btn-exit");
			root.Q<Label>("title-label").text = "Global Strategy";

			_btnPlay.clicked += () => _sceneLoader.LoadSelectCountry();
			_btnResume.clicked += OnResume;
			_btnLoad.clicked += () => _loadWindow?.Show();
			_btnSettings.clicked += () => _settingsWindow?.Show();
			_btnExit.clicked += () => Application.Quit();

			if (_loadWindow != null) {
				_loadWindow.SavesChanged += RefreshSaveButtons;
			}

			RefreshTexts();
			RefreshSaveButtons();
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			_loc.SetLocale(_state.Locale.Locale);
			RefreshTexts();
		}

		void RefreshTexts() {
			if (_btnPlay == null) {
				return;
			}
			var root = _doc.rootVisualElement;
			root.Q<Label>("title-label").text = _loc.Get("menu.title");
			_btnPlay.text = _loc.Get("menu.play");
			_btnResume.text = _loc.Get("menu.resume");
			_btnLoad.text = _loc.Get("menu.load");
			_btnSettings.text = _loc.Get("menu.settings");
			_btnExit.text = _loc.Get("menu.exit");
		}

		void RefreshSaveButtons() {
			bool hasSaves = _saveFileManager?.GetLastSave() != null;
			_btnResume.style.display = hasSaves ? DisplayStyle.Flex : DisplayStyle.None;
			_btnLoad.style.display = hasSaves ? DisplayStyle.Flex : DisplayStyle.None;
		}

		void OnResume() {
			var last = _saveFileManager?.GetLastSave();
			if (last != null) {
				_sceneLoader.LoadGame(saveName: last.SaveName);
			}
		}
	}
}
