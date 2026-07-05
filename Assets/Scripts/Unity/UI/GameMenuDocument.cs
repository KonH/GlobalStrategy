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
		ILocalization _loc;
		IFlyTextNotifier _flyText;
		UIDocument _doc;
		VisualElement _root;

		Label _lblTitle;
		Button _btnResume;
		Button _btnSave;
		Button _btnExit;

		[Inject]
		void Construct(IWriteOnlyCommandAccessor commands, VisualState visualState, SceneLoader sceneLoader, ILocalization loc, IFlyTextNotifier flyText) {
			_commands = commands;
			_visualState = visualState;
			_sceneLoader = sceneLoader;
			_loc = loc;
			_flyText = flyText;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void OnEnable() {
			if (_visualState != null) {
				_visualState.Locale.PropertyChanged += HandleLocaleChanged;
				_visualState.SaveResult.PropertyChanged += HandleSaveResultChanged;
			}
		}

		void OnDisable() {
			if (_visualState != null) {
				_visualState.Locale.PropertyChanged -= HandleLocaleChanged;
				_visualState.SaveResult.PropertyChanged -= HandleSaveResultChanged;
			}
		}

		void Start() {
			_root = _doc.rootVisualElement;
			_lblTitle = _root.Q<Label>("menu-title");
			_btnResume = _root.Q<Button>("btn-resume");
			_btnSave = _root.Q<Button>("btn-save");
			_btnExit = _root.Q<Button>("btn-exit");

			_btnResume.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnResume.ContainsPoint(e.localPosition)) { Hide(); }
			});
			_btnSave.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnSave.ContainsPoint(e.localPosition)) { OnSave(); }
			});
			_btnExit.RegisterCallback<PointerUpEvent>(e => {
				if (e.button == 0 && _btnExit.ContainsPoint(e.localPosition)) { _sceneLoader.LoadMainMenu(); }
			});

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
			Debug.Log("[FlyText] GameMenuDocument.OnSave: pushing SaveGameCommand");
			_commands?.Push(new SaveGameCommand());
		}

		void HandleLocaleChanged(object sender, PropertyChangedEventArgs e) {
			RefreshTexts();
		}

		void HandleSaveResultChanged(object sender, PropertyChangedEventArgs e) {
			var result = _visualState.SaveResult;
			if (result.Success) {
				_flyText?.Notify("game_menu.save.confirmation");
			} else {
				_flyText?.Notify("game_menu.save.error", result.ErrorType);
			}
		}

		void RefreshTexts() {
			if (_lblTitle == null) {
				return;
			}
			_lblTitle.text = _loc.Get("game_menu.title");
			_btnResume.text = _loc.Get("game_menu.resume");
			_btnSave.text = _loc.Get("game_menu.save");
			_btnExit.text = _loc.Get("game_menu.exit");
		}
	}
}
