using System.ComponentModel;
using GS.Game.Commands;
using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(PanelRenderer))]
	public class GameMenuDocument : MonoBehaviour {
		IWriteOnlyCommandAccessor _commands;
		VisualState _visualState;
		SceneLoader _sceneLoader;
		ILocalization _loc;
		IFlyTextNotifier _flyText;
		ModalState _modalState;
		PanelRenderer _doc;
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
			_doc = GetComponent<PanelRenderer>();
			_doc.sortingOrder = SortingOrder;
			_doc.RegisterUIReloadCallback(OnUIReload);
		}

		void OnDestroy() {
			if (_doc != null) {
				_doc.UnregisterUIReloadCallback(OnUIReload);
			}
		}

		void OnUIReload(PanelRenderer _, VisualElement rootElement) {
			bool keepVisible = _root != null && _root.style.display == DisplayStyle.Flex;
			_root = rootElement;
			_view = new GameMenuView(_root);
			_view.BtnResume.OnClick(Hide);
			_view.BtnSave.OnClick(OnSave);
			_view.BtnExit.OnClick(() => _sceneLoader.LoadMainMenu());
			if (keepVisible) {
				RefreshTexts();
				_root.style.display = DisplayStyle.Flex;
			} else {
				_root.style.display = DisplayStyle.None;
			}
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
			if (_root == null) {
				OnUIReload(_doc, PanelRendererRoot.Get(_doc));
			}
			if (_view == null && _root != null) {
				_view = new GameMenuView(_root);
				_view.BtnResume.OnClick(Hide);
				_view.BtnSave.OnClick(OnSave);
				_view.BtnExit.OnClick(() => _sceneLoader.LoadMainMenu());
			}
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
		}

		void Update() {
			if (_root == null) {
				return;
			}
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
