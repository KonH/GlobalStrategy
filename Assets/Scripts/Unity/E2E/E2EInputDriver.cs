using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace GS.Unity.E2E {
	public static class E2EInputDriver {
		// Frames granted to InputForUI to route the queued release into the panel and dispatch it.
		const int DeviceSettleFrames = 3;

		public static IEnumerator Click(VisualElement element, System.Action<string> setInputPath, System.Action<string> fail) {
			if (!E2EElementResolver.TryScreenPoint(element, out var screenPoint, out var error)) {
				fail(error);
				yield break;
			}

			bool deviceHitTarget = false;
			EventCallback<PointerUpEvent> probe = evt => {
				if (evt.button == 0 && element.ContainsPoint(evt.localPosition)) {
					deviceHitTarget = true;
				}
			};

			var mouse = Mouse.current;
			if (mouse != null) {
				element.RegisterCallback<PointerUpEvent>(probe);
				QueueMouse(mouse, screenPoint, pressed: false);
				InputSystem.Update();
				yield return null;
				QueueMouse(mouse, screenPoint, pressed: true);
				InputSystem.Update();
				yield return null;
				QueueMouse(mouse, screenPoint, pressed: false);
				InputSystem.Update();
				for (int i = 0; i < DeviceSettleFrames && !deviceHitTarget; i++) {
					yield return null;
				}
				element.UnregisterCallback<PointerUpEvent>(probe);
			}

			if (deviceHitTarget) {
				setInputPath("device");
				yield break;
			}

			if (NeedsPanelFallback(element)) {
				SendPanelClick(element);
				setInputPath("panel");
				yield return null;
				yield break;
			}

			fail(mouse == null
				? "No mouse device is available and the panel fallback could not address the element."
				: "The device click did not reach the element and the panel fallback could not address it either.");
		}

		public static IEnumerator SetValue(VisualElement element, string value, System.Action<string> setInputPath, System.Action<string> fail) {
			bool clickUsedPanel = false;
			System.Action<string> trackClickPath = path => {
				clickUsedPanel = path == "panel";
				setInputPath(path);
			};
			yield return Click(element, trackClickPath, fail);

			var keyboard = Keyboard.current;
			if (keyboard != null && !string.IsNullOrEmpty(value)) {
				foreach (char c in value) {
					InputSystem.QueueTextEvent(keyboard, c);
					InputSystem.Update();
				}
				// Typing on the device path does not restore fidelity a panel-path click already lost.
				setInputPath(clickUsedPanel ? "panel" : "device");
				yield return null;
				yield break;
			}

			if (element is TextField textField) {
				textField.value = value;
				setInputPath("panel");
			} else {
				fail("setValue target is not a TextField and no keyboard device is available.");
			}
		}

		static bool NeedsPanelFallback(VisualElement element) {
			return element != null
				&& element.panel != null
				&& element.enabledInHierarchy
				&& element.worldBound.width > 0
				&& element.worldBound.height > 0;
		}

		static void QueueMouse(Mouse mouse, Vector2 screenPoint, bool pressed) {
			var state = new MouseState { position = screenPoint }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, pressed);
			InputSystem.QueueStateEvent(mouse, state);
		}

		static void SendPanelClick(VisualElement element) {
			// mousePosition is read as a panel-space point: dispatch derives each handler's
			// localPosition from it via WorldToLocal, so a pre-converted point lands outside.
			Vector2 panelPoint = element.worldBound.center;
			var nativeDown = new Event {
				type = EventType.MouseDown,
				mousePosition = panelPoint,
				button = 0
			};
			using (var down = PointerDownEvent.GetPooled(nativeDown)) {
				down.target = element;
				element.SendEvent(down);
			}
			var nativeUp = new Event {
				type = EventType.MouseUp,
				mousePosition = panelPoint,
				button = 0
			};
			using (var up = PointerUpEvent.GetPooled(nativeUp)) {
				up.target = element;
				element.SendEvent(up);
			}
		}
	}
}
