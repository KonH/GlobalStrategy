using System;
using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class TimeView {
		readonly Label _date;
		readonly Button _btnPause, _btnX1, _btnX2, _btnX3;

		public TimeView(VisualElement root, Action onPauseToggle, Action<int> onSpeedChange) {
			_date = root.Q<Label>("time-date");
			_btnPause = root.Q<Button>("btn-pause");
			_btnX1 = root.Q<Button>("btn-x1");
			_btnX2 = root.Q<Button>("btn-x2");
			_btnX3 = root.Q<Button>("btn-x3");
			_btnPause.clicked += onPauseToggle;
			_btnX1.clicked += () => onSpeedChange(0);
			_btnX2.clicked += () => onSpeedChange(1);
			_btnX3.clicked += () => onSpeedChange(2);
		}

		public void Refresh(TimeState state) {
			_date.text = state.CurrentTime.ToString("HH:00 dd/MM/yyyy");
			if (state.IsPaused) {
				_btnPause.RemoveFromClassList("gs-icon-btn--pause");
				_btnPause.AddToClassList("gs-icon-btn--play");
				_btnPause.AddToClassList("gs-btn--active");
			} else {
				_btnPause.RemoveFromClassList("gs-icon-btn--play");
				_btnPause.AddToClassList("gs-icon-btn--pause");
				_btnPause.RemoveFromClassList("gs-btn--active");
			}
			SetSpeedActive(_btnX1, state.MultiplierIndex == 0, state.IsPaused);
			SetSpeedActive(_btnX2, state.MultiplierIndex == 1, state.IsPaused);
			SetSpeedActive(_btnX3, state.MultiplierIndex == 2, state.IsPaused);
		}

		void SetSpeedActive(Button btn, bool active, bool paused) {
			btn.RemoveFromClassList("gs-btn--active");
			btn.RemoveFromClassList("gs-btn--speed");
			if (active && !paused) {
				btn.AddToClassList("gs-btn--speed");
			}
		}
	}
}
