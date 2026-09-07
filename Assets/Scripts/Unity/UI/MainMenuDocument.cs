using System.ComponentModel;
using GS.Game.Configs;
using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(PanelRenderer))]
	public class MainMenuDocument : MonoBehaviour {
		const string AboutUrl = "https://konh.github.io/hidden-council/";

		[SerializeField] string _versionName;

		SaveFileManager _saveFileManager;
		SceneLoader _sceneLoader;
		LoadWindowDocument _loadWindow;
		SettingsWindowDocument _settingsWindow;
		VisualState _state;
		ILocalization _loc;
		GameSettings _gameSettings;
		PanelRenderer _doc;
		MainMenuView _view;

		[Inject]
		void Construct(SaveFileManager saveFileManager, SceneLoader sceneLoader, LoadWindowDocument loadWindow, SettingsWindowDocument settingsWindow, VisualState state, ILocalization loc, GameSettings gameSettings) {
			_saveFileManager = saveFileManager;
			_sceneLoader = sceneLoader;
			_loadWindow = loadWindow;
			_settingsWindow = settingsWindow;
			_state = state;
			_loc = loc;
			_gameSettings = gameSettings;
		}

		void Awake() {
			_doc = GetComponent<PanelRenderer>();
			_doc.RegisterUIReloadCallback(OnUIReload);
		}

		void OnDestroy() {
			if (_doc != null) {
				_doc.UnregisterUIReloadCallback(OnUIReload);
			}
		}

		void OnUIReload(PanelRenderer _, VisualElement rootElement) {
			Bind(rootElement);
		}

		void Bind(VisualElement root) {
			if (root == null || _loc == null) {
				return;
			}
			_view = new MainMenuView(root);
			_view.SetVersion(_versionName, $"v{_gameSettings.Version}");

			_view.BtnPlay.OnClick(() => _sceneLoader.LoadSelectCountry());
			_view.BtnResume.OnClick(OnResume);
			_view.BtnLoad.OnClick(() => _loadWindow?.Show());
			_view.BtnSettings.OnClick(() => _settingsWindow?.Show());
			_view.BtnAbout.OnClick(() => Application.OpenURL(AboutUrl));
			_view.BtnExit.OnClick(Application.Quit);

			RefreshTexts();
			RefreshSaveButtons();
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
			if (_view == null) {
				Bind(PanelRendererRoot.Get(_doc));
			}

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
			_view?.RefreshTexts(_loc);
		}

		void RefreshSaveButtons() {
			bool hasSaves = _saveFileManager?.GetLastSave() != null;
			_view?.Refresh(hasSaves);
		}

		void OnResume() {
			var last = _saveFileManager?.GetLastSave();
			if (last != null) {
				_sceneLoader.LoadGame(saveName: last.SaveName);
			}
		}
	}
}
