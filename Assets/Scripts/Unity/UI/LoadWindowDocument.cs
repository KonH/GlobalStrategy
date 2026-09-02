using GS.Main;
using GS.Unity.Common;
using UnityEngine;
using UnityEngine.UIElements;
using VContainer;

namespace GS.Unity.UI {
	[RequireComponent(typeof(UIDocument))]
	public class LoadWindowDocument : MonoBehaviour {
		SaveFileManager _saveFileManager;
		SceneLoader _sceneLoader;
		ILocalization _loc;
		UIDocument _doc;
		LoadWindowView _view;

		public event System.Action SavesChanged;

		[Inject]
		void Construct(SaveFileManager saveFileManager, SceneLoader sceneLoader, ILocalization loc) {
			_saveFileManager = saveFileManager;
			_sceneLoader = sceneLoader;
			_loc = loc;
		}

		void Awake() {
			_doc = GetComponent<UIDocument>();
		}

		void Start() {
			var root = _doc.rootVisualElement;
			_view = new LoadWindowView(root, _loc, OnLoadSave, OnDeleteSave);
			_view.BtnBack.OnClick(Hide);
			_view.RefreshTexts();
			Hide();
		}

		public void Show() {
			_doc.rootVisualElement.style.display = DisplayStyle.Flex;
			BuildList();
		}

		public void Hide() {
			_doc.rootVisualElement.style.display = DisplayStyle.None;
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
