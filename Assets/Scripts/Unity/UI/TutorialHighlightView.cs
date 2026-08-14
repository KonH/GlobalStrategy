#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;
using GS.Main;

namespace GS.Unity.UI {
	class TutorialHighlightView {
		const float ArrowWidth = 44f;
		const float ArrowHeight = 40f;
		const float ArrowGap = 12f;
		// Minimum clear space (in root-local pixels) required to place the arrow on a given side.
		const float SideClearance = ArrowGap + ArrowHeight;
		const float TravelAmplitude = 14f;
		const float CycleDurationMs = 1400f;
		const long AnimIntervalMs = 16;

		enum ArrowSide {
			Above,
			Below,
			Left,
			Right
		}

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
			_phase = Mathf.Repeat(_phase + (AnimIntervalMs / CycleDurationMs), 1f);
			float travel = BounceEase(_phase) * TravelAmplitude;
			PositionAt(target, travel);
		}

		void PositionAt(VisualElement target, float travel) {
			Rect targetBound = target.worldBound;
			Rect rootBound = _root.worldBound;
			ArrowSide side = ChooseSide(rootBound, targetBound);

			Vector2 restCenter;
			Vector2 travelVector;
			float rotationDegrees;
			switch (side) {
				case ArrowSide.Below:
					restCenter = new Vector2(targetBound.center.x, targetBound.yMax + ArrowGap + (ArrowHeight * 0.5f));
					travelVector = new Vector2(0f, -travel);
					rotationDegrees = 270f; // points up, toward the target above it
					break;
				case ArrowSide.Left:
					restCenter = new Vector2(targetBound.xMin - ArrowGap - (ArrowWidth * 0.5f), targetBound.center.y);
					travelVector = new Vector2(travel, 0f);
					rotationDegrees = 0f; // points right, toward the target to its right
					break;
				case ArrowSide.Right:
					restCenter = new Vector2(targetBound.xMax + ArrowGap + (ArrowWidth * 0.5f), targetBound.center.y);
					travelVector = new Vector2(-travel, 0f);
					rotationDegrees = 180f; // points left, toward the target to its left
					break;
				case ArrowSide.Above:
				default:
					restCenter = new Vector2(targetBound.center.x, targetBound.yMin - ArrowGap - (ArrowHeight * 0.5f));
					travelVector = new Vector2(0f, travel);
					rotationDegrees = 90f; // points down, toward the target below it
					break;
			}

			Vector2 worldCenter = restCenter + travelVector;
			Vector2 localCenter = _root.WorldToLocal(worldCenter);
			_arrow.style.left = localCenter.x - (ArrowWidth * 0.5f);
			_arrow.style.top = localCenter.y - (ArrowHeight * 0.5f);
			_arrow.style.rotate = new StyleRotate(new Rotate(rotationDegrees));
		}

		static ArrowSide ChooseSide(Rect rootBound, Rect targetBound) {
			float spaceAbove = targetBound.yMin - rootBound.yMin;
			if (spaceAbove >= SideClearance) {
				return ArrowSide.Above;
			}
			float spaceBelow = rootBound.yMax - targetBound.yMax;
			if (spaceBelow >= SideClearance) {
				return ArrowSide.Below;
			}
			float spaceRight = rootBound.xMax - targetBound.xMax;
			if (spaceRight >= SideClearance) {
				return ArrowSide.Right;
			}
			return ArrowSide.Left;
		}

		// Bounces toward the target (0 -> 1) over the first half of the cycle, then eases
		// smoothly back to rest (1 -> 0) over the second half.
		static float BounceEase(float t) {
			if (t < 0.5f) {
				return EaseOutBounce(t / 0.5f);
			}
			return 1f - EaseOutCubic((t - 0.5f) / 0.5f);
		}

		static float EaseOutCubic(float x) {
			return 1f - Mathf.Pow(1f - x, 3f);
		}

		static float EaseOutBounce(float x) {
			const float n1 = 7.5625f;
			const float d1 = 2.75f;
			if (x < 1f / d1) {
				return n1 * x * x;
			}
			if (x < 2f / d1) {
				x -= 1.5f / d1;
				return (n1 * x * x) + 0.75f;
			}
			if (x < 2.5f / d1) {
				x -= 2.25f / d1;
				return (n1 * x * x) + 0.9375f;
			}
			x -= 2.625f / d1;
			return (n1 * x * x) + 0.984375f;
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
