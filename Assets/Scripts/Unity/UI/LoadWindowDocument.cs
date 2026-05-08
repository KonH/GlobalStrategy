using System.Collections.Generic;
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
		ScrollView _saveList;
		Label _titleLabel;
		Button _btnBack;

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
			_saveList = root.Q<ScrollView>("save-list");
			_titleLabel = root.Q<Label>("window-title");
			_btnBack = root.Q<Button>("btn-back");
			_btnBack.clicked += Hide;
			RefreshTexts();
			Hide();
		}

		void RefreshTexts() {
			if (_titleLabel != null) {
				_titleLabel.text = _loc.Get("load.title");
			}
			if (_btnBack != null) {
				_btnBack.text = _loc.Get("load.back");
			}
		}

		public void Show() {
			_doc.rootVisualElement.style.display = DisplayStyle.Flex;
			BuildList();
		}

		public void Hide() {
			_doc.rootVisualElement.style.display = DisplayStyle.None;
		}

		void BuildList() {
			_saveList.Clear();
			IReadOnlyList<SaveFileInfo> saves = _saveFileManager.ListSaves();
			foreach (var save in saves) {
				_saveList.Add(BuildRow(save));
			}
			if (saves.Count == 0) {
				var empty = new Label(_loc.Get("load.no_saves"));
				empty.AddToClassList("save-country");
				_saveList.Add(empty);
			}
		}

		VisualElement BuildRow(SaveFileInfo save) {
			var row = new VisualElement();
			row.AddToClassList("save-row");

			var info = new VisualElement();
			info.AddToClassList("save-row-info");

			var country = new Label(save.OrganizationId);
			country.AddToClassList("gs-label");
			country.AddToClassList("save-country");
			info.Add(country);

			var date = new Label(save.GameDate.ToString("yyyy-MM-dd"));
			date.AddToClassList("gs-content");
			date.AddToClassList("save-date");
			info.Add(date);

			row.Add(info);

			var btnLoad = new Button(() => _sceneLoader.LoadGame(saveName: save.SaveName));
			btnLoad.text = _loc.Get("load.btn_load");
			btnLoad.AddToClassList("gs-btn");
			btnLoad.AddToClassList("gs-btn--small");
			btnLoad.AddToClassList("row-button");
			row.Add(btnLoad);

			var saveName = save.SaveName;
			var btnDelete = new Button(() => {
				_saveFileManager.DeleteSave(saveName);
				SavesChanged?.Invoke();
				BuildList();
			});
			btnDelete.text = _loc.Get("load.btn_delete");
			btnDelete.AddToClassList("gs-btn");
			btnDelete.AddToClassList("gs-btn--destructive");
			btnDelete.AddToClassList("row-button");
			row.Add(btnDelete);

			return row;
		}
	}
}
