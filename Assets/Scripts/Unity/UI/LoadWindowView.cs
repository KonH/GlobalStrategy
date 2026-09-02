using System;
using System.Collections;
using System.Collections.Generic;
using GS.Main;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	/// <summary>
	/// Plain view for LoadWindowDocument (Docs/Specs/26_08_28_16_ui-refactoring phase 7, "the six
	/// view-less documents" batch). Owns the ListView makeItem/bindItem pair and the per-row
	/// Load/Delete buttons; the document only supplies the save list plus load/delete callbacks and
	/// keeps DI, scene loading and the SavesChanged event.
	/// </summary>
	public class LoadWindowView {
		class SaveRowState {
			public SaveFileInfo Save;
		}

		readonly ILocalization _loc;
		readonly Action<SaveFileInfo> _onLoad;
		readonly Action<SaveFileInfo> _onDelete;
		readonly ListView _saveList;
		readonly Label _saveListEmpty;
		readonly Label _titleLabel;
		readonly Button _btnBack;
		IReadOnlyList<SaveFileInfo> _currentSaves = Array.Empty<SaveFileInfo>();

		public LoadWindowView(VisualElement root, ILocalization loc, Action<SaveFileInfo> onLoad, Action<SaveFileInfo> onDelete) {
			_loc = loc;
			_onLoad = onLoad;
			_onDelete = onDelete;
			_saveList = root.Q<ListView>("save-list");
			_saveListEmpty = root.Q<Label>("save-list-empty");
			_titleLabel = root.Q<Label>("window-title");
			_btnBack = root.Q<Button>("btn-back");
			if (_saveList != null) {
				_saveList.makeItem = MakeRow;
				_saveList.bindItem = (element, index) => BindRow(element, _currentSaves[index]);
			}
		}

		public Button BtnBack => _btnBack;

		public void RefreshTexts() {
			if (_titleLabel != null) {
				_titleLabel.text = _loc.Get("load.title");
			}
			if (_btnBack != null) {
				_btnBack.text = _loc.Get("load.back");
			}
		}

		public void Refresh(IReadOnlyList<SaveFileInfo> saves) {
			_currentSaves = saves ?? Array.Empty<SaveFileInfo>();
			if (_saveList == null) {
				return;
			}
			_saveList.itemsSource = (IList)_currentSaves;
			_saveList.Rebuild();
			if (_saveListEmpty != null) {
				_saveListEmpty.style.display = _currentSaves.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
				_saveListEmpty.text = _loc.Get("load.no_saves");
			}
		}

		VisualElement MakeRow() {
			var row = new VisualElement();
			row.AddToClassList("save-row");

			var info = new VisualElement();
			info.AddToClassList("save-row-info");

			var country = new Label();
			country.AddToClassList("gs-label");
			country.AddToClassList("save-country");
			info.Add(country);

			var date = new Label();
			date.AddToClassList("gs-content");
			date.AddToClassList("save-date");
			info.Add(date);

			row.Add(info);

			var state = new SaveRowState();

			var btnLoad = new Button(() => _onLoad?.Invoke(state.Save));
			btnLoad.text = _loc.Get("load.btn_load");
			btnLoad.AddToClassList("gs-btn");
			btnLoad.AddToClassList("gs-btn--small");
			btnLoad.AddToClassList("row-button");
			row.Add(btnLoad);

			var btnDelete = new Button(() => _onDelete?.Invoke(state.Save));
			btnDelete.text = _loc.Get("load.btn_delete");
			btnDelete.AddToClassList("gs-btn");
			btnDelete.AddToClassList("gs-btn--destructive");
			btnDelete.AddToClassList("row-button");
			row.Add(btnDelete);

			row.userData = state;
			return row;
		}

		void BindRow(VisualElement row, SaveFileInfo save) {
			var state = (SaveRowState)row.userData;
			state.Save = save;
			var country = row.Q<Label>(className: "save-country");
			country.text = save.OrganizationId;
			var date = row.Q<Label>(className: "save-date");
			date.text = save.GameDate.ToString("yyyy-MM-dd");
		}
	}
}
