#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class TutorialHighlightView {
		const float ArrowSize = 40f;
		const float BobAmplitude = 12f;
		const float BobSpeed = 0.18f;
		const long AnimIntervalMs = 16;

		readonly VisualElement _root;
		readonly VisualElement _arrow;
		readonly Func<string, VisualElement?> _resolveTarget;
		IVisualElementScheduledItem? _animItem;
		float _phase;
		string? _activeTargetId;

		public TutorialHighlightView(VisualElement root, Func<string, VisualElement?> resolveTarget) {
			_root = root ?? throw new ArgumentNullException(nameof(root));
			_resolveTarget = resolveTarget ?? throw new ArgumentNullException(nameof(resolveTarget));
			_arrow = root.Q("tutorial-highlight-arrow") ?? root;
			_root.pickingMode = PickingMode.Ignore;
			_arrow.pickingMode = PickingMode.Ignore;
			_arrow.style.display = DisplayStyle.None;
			Hide();
		}

		public void Refresh(ActiveTasksState state) {
			_activeTargetId = FindHighlightTargetId(state);
			if (string.IsNullOrEmpty(_activeTargetId)) {
				Hide();
				return;
			}
			_root.style.display = DisplayStyle.Flex;
			EnsureAnimating();
			TickAnimation();
		}

		static string? FindHighlightTargetId(ActiveTasksState? state) {
			if (state == null) {
				return null;
			}
			foreach (var task in state.Tasks) {
				if (task.IsTutorial && !string.IsNullOrEmpty(task.HighlightTargetId)) {
					return task.HighlightTargetId;
				}
			}
			return null;
		}

		void EnsureAnimating() {
			if (_animItem != null) {
				return;
			}
			_animItem = _root.schedule.Execute(TickAnimation).Every(AnimIntervalMs);
		}

		void TickAnimation() {
			if (string.IsNullOrEmpty(_activeTargetId)) {
				Hide();
				return;
			}
			VisualElement? target = _resolveTarget(_activeTargetId);
			if (target == null || target.panel == null) {
				_arrow.style.display = DisplayStyle.None;
				return;
			}
			_arrow.style.display = DisplayStyle.Flex;
			_phase += BobSpeed;
			float offset = Mathf.Sin(_phase) * BobAmplitude;
			PositionAt(target, offset);
		}

		void PositionAt(VisualElement target, float bobOffset) {
			Rect bound = target.worldBound;
			Vector2 localCenter = _root.WorldToLocal(bound.center);
			Vector2 localTopCenter = _root.WorldToLocal(new Vector2(bound.center.x, bound.yMin));
			_arrow.style.left = localCenter.x - (ArrowSize * 0.5f);
			_arrow.style.top = localTopCenter.y - ArrowSize - 4f + bobOffset;
		}

		void Hide() {
			_root.style.display = DisplayStyle.None;
			_arrow.style.display = DisplayStyle.None;
			_activeTargetId = null;
			if (_animItem != null) {
				_animItem.Pause();
				_animItem = null;
			}
			_phase = 0f;
		}
	}
}
