using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(PanelRenderer))]
	public class LoadWindowDocument : MonoBehaviour {
		SaveFileManager _saveFileManager;
		SceneLoader _sceneLoader;
		ILocalization _loc;
		PanelRenderer _doc;
		VisualElement _root;
		LoadWindowView _view;

		public event System.Action SavesChanged;

		[Inject]
		void Construct(SaveFileManager saveFileManager, SceneLoader sceneLoader, ILocalization loc) {
			_saveFileManager = saveFileManager;
			_sceneLoader = sceneLoader;
			_loc = loc;
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
			bool keepVisible = _root != null && _root.style.display == DisplayStyle.Flex;
			_root = rootElement;
			_view = new LoadWindowView(_root, _loc, OnLoadSave, OnDeleteSave);
			_view.BtnBack.OnClick(Hide);
			_view.RefreshTexts();
			if (keepVisible) {
				Show();
			} else {
				Hide();
			}
		}

		void Start() {
			if (_view == null) {
				OnUIReload(_doc, PanelRendererRoot.Get(_doc));
			}
		}

		public void Show() {
			if (_root == null) {
				return;
			}
			_root.style.display = DisplayStyle.Flex;
			BuildList();
		}

		public void Hide() {
			if (_root != null) {
				_root.style.display = DisplayStyle.None;
			}
		}

		void BuildList() {
			_view?.Refresh(_saveFileManager.ListSaves());
		}

		void OnLoadSave(SaveFileInfo save) {
			_sceneLoader.LoadGame(saveName: save.SaveName);
		}

		void OnDeleteSave(SaveFileInfo save) {
			_saveFileManager.DeleteSave(save.SaveName);
			SavesChanged?.Invoke();
			BuildList();
		}
	}
}
