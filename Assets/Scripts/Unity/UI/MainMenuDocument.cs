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
		Label _versionLabel;

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
			_versionLabel = root.Q<Label>("version-label");
			if (_versionLabel != null) {
				_versionLabel.text = $"v{Application.version}";
			}

			_btnPlay.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnPlay.ContainsPoint(e.localPosition)) _sceneLoader.LoadSelectCountry(); });
			_btnResume.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnResume.ContainsPoint(e.localPosition)) OnResume(); });
			_btnLoad.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnLoad.ContainsPoint(e.localPosition)) _loadWindow?.Show(); });
			_btnSettings.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnSettings.ContainsPoint(e.localPosition)) _settingsWindow?.Show(); });
			_btnExit.RegisterCallback<PointerUpEvent>(e => { if (e.button == 0 && _btnExit.ContainsPoint(e.localPosition)) Application.Quit(); });

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
