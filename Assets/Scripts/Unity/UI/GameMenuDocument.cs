using System.ComponentModel;
using GS.Game.Commands;
using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class GameMenuDocument : MonoBehaviour {
		IWriteOnlyCommandAccessor _commands;
		VisualState _visualState;
		SceneLoader _sceneLoader;
		SettingsWindowDocument _settingsWindow;
		ILocalization _loc;
		UIDocument _doc;
		VisualElement _root;

		Label _lblTitle;
		Button _btnResume;
		Button _btnSave;
		Button _btnSettings;
		Button _btnMainMenu;

		[Inject]
		void Construct(IWriteOnlyCommandAccessor commands, VisualState visualState, SceneLoader sceneLoader, SettingsWindowDocument settingsWindow, ILocalization loc) {
			_commands = commands;
			_visualState = visualState;
			_sceneLoader = sceneLoader;
			_settingsWindow = settingsWindow;
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
			_lblTitle = _root.Q<Label>("menu-title");
			_btnResume = _root.Q<Button>("btn-resume");
			_btnSave = _root.Q<Button>("btn-save");
			_btnSettings = _root.Q<Button>("btn-settings");
			_btnMainMenu = _root.Q<Button>("btn-main-menu");

			_btnResume.clicked += Hide;
			_btnSave.clicked += OnSave;
			_btnSettings.clicked += () => _settingsWindow?.Show();
			_btnMainMenu.clicked += () => _sceneLoader.LoadMainMenu();

			Hide();
		}

		void Update() {
			var keyboard = Keyboard.current;
			if (keyboard == null) {
				return;
			}
			if (keyboard.escapeKey.wasPressedThisFrame) {
				if (_root.style.display == DisplayStyle.None) {
					Show();
				} else {
					Hide();
				}
			}
		}

		public void Show() {
			_commands?.Push(new PauseCommand());
			ModalState.IsModalOpen = true;
			RefreshTexts();
			_root.style.display = DisplayStyle.Flex;
		}

		void Hide() {
			_commands?.Push(new UnpauseCommand());
			ModalState.IsModalOpen = false;
			_root.style.display = DisplayStyle.None;
		}

		void OnSave() {
			_commands?.Push(new SaveGameCommand());
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
		}

		void RefreshTexts() {
			if (_lblTitle == null) {
				return;
			}
			_lblTitle.text = _loc.Get("game_menu.title");
			_btnResume.text = _loc.Get("game_menu.resume");
			_btnSave.text = _loc.Get("game_menu.save");
			_btnSettings.text = _loc.Get("game_menu.settings");
			_btnMainMenu.text = _loc.Get("game_menu.main_menu");
		}
	}
}
