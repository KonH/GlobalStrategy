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
		ModalState _modalState;
		UIDocument _doc;
		VisualElement _root;
		GameMenuView _view;

		[Inject]
		void Construct(IWriteOnlyCommandAccessor commands, VisualState visualState, SceneLoader sceneLoader, ILocalization loc, IFlyTextNotifier flyText, ModalState modalState) {
			_commands = commands;
			_visualState = visualState;
			_sceneLoader = sceneLoader;
			_loc = loc;
			_flyText = flyText;
			_modalState = modalState;
		}

		// Explicit sortingOrder, not scene-authoring order — see .claude/rules/unity/uitoolkit.md
		// "Layer Model" (sortingOrder governs stacking among documents sharing HUDPanelSettings;
		// Above modals (Leaderboard 500 / Goals 505 / War 510), just below FlyText (1000), below EndGame (1100).
		const int SortingOrder = 990;

		void Awake() {
			_doc = GetComponent<UIDocument>();
			_doc.sortingOrder = SortingOrder;
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
			_view = new GameMenuView(_root);

			_view.BtnResume.OnClick(Hide);
			_view.BtnSave.OnClick(OnSave);
			_view.BtnExit.OnClick(() => _sceneLoader.LoadMainMenu());

			Hide();
		}

		void Update() {
			var keyboard = Keyboard.current;
			if (keyboard == null) {
				return;
			}
			if (keyboard.escapeKey.wasPressedThisFrame) {
				if (_root.style.display == DisplayStyle.None) {
					if (!_modalState.IsLocked()) {
						Show();
					}
				} else {
					Hide();
				}
			}
		}

		public void Show() {
			if (_root == null
				|| _root.style.display != DisplayStyle.None
				|| _modalState.IsLocked()) {
				return;
			}
			_commands?.Push(new PauseCommand());
			_modalState.Lock(this);
			RefreshTexts();
			_root.style.display = DisplayStyle.Flex;
		}

		public bool IsVisible => _root != null && _root.style.display == DisplayStyle.Flex;

		void Hide() {
			_commands?.Push(new UnpauseCommand());
			_modalState.Unlock(this);
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
			_view?.RefreshTexts(_loc);
		}
	}
}
