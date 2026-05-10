using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GS.Unity.UI {
	class TooltipEntry {
		public VisualElement Panel;
		public string Id;
		public HashSet<string> Ancestors;
		public bool IsPinned;
		public float ElapsedSeconds;
		public bool IsPointerOverPanel;
		public bool IsPointerOverTrigger;
	}

	class TooltipSystem {
		const float AutoPinSeconds = 2f;
		const long HideDelayMs = 80;
		readonly VisualElement _hudRoot;
		readonly List<TooltipEntry> _stack = new();

		public TooltipSystem(VisualElement hudRoot) {
			_hudRoot = hudRoot;
		}

		public void RegisterTrigger(VisualElement trigger, string id, Func<TooltipContext, VisualElement> buildContent, HashSet<string> ancestors) {
			if (ancestors.Contains(id)) {
				return;
			}

			TooltipEntry ownedEntry = null;

			trigger.RegisterCallback<PointerEnterEvent>(_ => {
				// After a resource refresh the label is recreated; reuse the pinned entry rather than resetting it
				if (_stack.Count > 0 && _stack[_stack.Count - 1].Id == id && _stack[_stack.Count - 1].IsPinned) {
					ownedEntry = _stack[_stack.Count - 1];
					PositionNear(ownedEntry.Panel, trigger);
					ownedEntry.IsPointerOverTrigger = true;
					return;
				}
				if (ownedEntry != null) {
					ownedEntry.IsPointerOverTrigger = false;
				}
				CloseUntilAncestorOf(ancestors);
				ownedEntry = OpenTooltip(trigger, id, buildContent, ancestors);
				ownedEntry.IsPointerOverTrigger = true;
			});

			trigger.RegisterCallback<PointerLeaveEvent>(_ => {
				if (ownedEntry != null) {
					ownedEntry.IsPointerOverTrigger = false;
				}
				var localEntry = ownedEntry;
				// Delay the close so PointerEnterEvent on the panel can fire first (crosses the 4px gap)
				_hudRoot.schedule.Execute(() => ScheduledClose(localEntry)).StartingIn(HideDelayMs);
			});
		}

		public void Update(float deltaTime) {
			var mouse = Mouse.current;
			if (mouse == null) {
				return;
			}

			for (int i = 0; i < _stack.Count; i++) {
				var entry = _stack[i];
				if (!entry.IsPinned) {
					entry.ElapsedSeconds += deltaTime;
					if (entry.ElapsedSeconds >= AutoPinSeconds) {
						SetPinned(entry, true);
					}
				}
			}

			if (mouse.middleButton.wasPressedThisFrame) {
				for (int i = _stack.Count - 1; i >= 0; i--) {
					var entry = _stack[i];
					if (entry.IsPointerOverPanel || entry.IsPointerOverTrigger) {
						SetPinned(entry, true);
						break;
					}
				}
			}

			if (mouse.leftButton.wasPressedThisFrame) {
				bool anyPinned = false;
				bool pointerOverAny = false;
				for (int i = 0; i < _stack.Count; i++) {
					if (_stack[i].IsPinned) {
						anyPinned = true;
					}
					if (_stack[i].IsPointerOverPanel || _stack[i].IsPointerOverTrigger) {
						pointerOverAny = true;
					}
				}
				if (anyPinned && !pointerOverAny) {
					int firstPinned = -1;
					for (int i = 0; i < _stack.Count; i++) {
						if (_stack[i].IsPinned) {
							firstPinned = i;
							break;
						}
					}
					if (firstPinned >= 0) {
						while (_stack.Count > firstPinned) {
							CloseTop();
						}
					}
				}
			}
		}

		void ScheduledClose(TooltipEntry entry) {
			if (entry == null || !_stack.Contains(entry)) {
				return;
			}
			if (!entry.IsPinned && !entry.IsPointerOverPanel && !entry.IsPointerOverTrigger) {
				CloseEntry(entry);
			}
		}

		void SetPinned(TooltipEntry entry, bool pinned) {
			entry.IsPinned = pinned;
			if (pinned) {
				entry.Panel.AddToClassList("tooltip-overlay--pinned");
			} else {
				entry.Panel.RemoveFromClassList("tooltip-overlay--pinned");
			}
		}

		void CloseUntilAncestorOf(HashSet<string> ancestors) {
			while (_stack.Count > 0) {
				var top = _stack[_stack.Count - 1];
				// Stop at ancestors and at pinned entries — pinned tooltips only dismiss on click-outside
				if (ancestors.Contains(top.Id) || top.IsPinned) {
					break;
				}
				CloseTop();
			}
		}

		TooltipEntry OpenTooltip(VisualElement trigger, string id, Func<TooltipContext, VisualElement> buildContent, HashSet<string> ancestors) {
			var panel = new VisualElement();
			panel.AddToClassList("tooltip-overlay");

			var entry = new TooltipEntry {
				Panel = panel,
				Id = id,
				Ancestors = ancestors,
				IsPinned = false,
				ElapsedSeconds = 0f,
				IsPointerOverPanel = false,
				IsPointerOverTrigger = false
			};

			panel.RegisterCallback<PointerEnterEvent>(_ => {
				entry.IsPointerOverPanel = true;
			});
			panel.RegisterCallback<PointerLeaveEvent>(_ => {
				entry.IsPointerOverPanel = false;
				var localEntry = entry;
				_hudRoot.schedule.Execute(() => ScheduledClose(localEntry)).StartingIn(HideDelayMs);
			});

			_hudRoot.Add(panel);
			PositionNear(panel, trigger);

			var innerAncestors = new HashSet<string>(ancestors) { id };
			var context = new TooltipContext(this, innerAncestors);
			panel.Add(buildContent(context));

			_stack.Add(entry);
			return entry;
		}

		void CloseEntry(TooltipEntry entry) {
			int idx = _stack.IndexOf(entry);
			if (idx < 0) {
				return;
			}
			while (_stack.Count > idx) {
				CloseTop();
			}
		}

		void CloseTop() {
			if (_stack.Count == 0) {
				return;
			}
			var top = _stack[_stack.Count - 1];
			_hudRoot.Remove(top.Panel);
			_stack.RemoveAt(_stack.Count - 1);
		}

		void PositionNear(VisualElement panel, VisualElement trigger) {
			panel.style.left = trigger.worldBound.xMin;
			panel.style.top = trigger.worldBound.yMax + 4;
			panel.RegisterCallback<GeometryChangedEvent>(_ => AdjustPosition(panel, trigger));
		}

		void AdjustPosition(VisualElement panel, VisualElement trigger) {
			var screen = _hudRoot.worldBound;
			var t = trigger.worldBound;
			var p = panel.worldBound;

			float top = t.yMax + 4;
			if (top + p.height > screen.yMax) {
				top = t.yMin - p.height - 4;
			}
			top = UnityEngine.Mathf.Max(top, screen.yMin);

			float left = t.xMin;
			if (left + p.width > screen.xMax) {
				left = screen.xMax - p.width;
			}
			left = UnityEngine.Mathf.Max(left, screen.xMin);

			panel.style.left = left;
			panel.style.top = top;
		}
	}
}
