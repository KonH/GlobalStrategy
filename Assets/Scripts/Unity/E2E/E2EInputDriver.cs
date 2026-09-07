using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

namespace GS.Unity.E2E {
	public static class E2EInputDriver {
		public static IEnumerator Click(VisualElement element, System.Action<string> setInputPath, System.Action<string> fail) {
			if (!E2EElementResolver.TryScreenPoint(element, out var screenPoint, out var error)) {
				fail(error);
				yield break;
			}

			bool usedDevice = false;
			var mouse = Mouse.current;
			if (mouse != null) {
				QueueMouse(mouse, screenPoint, pressed: false);
				InputSystem.Update();
				yield return null;
				QueueMouse(mouse, screenPoint, pressed: true);
				InputSystem.Update();
				yield return null;
				QueueMouse(mouse, screenPoint, pressed: false);
				InputSystem.Update();
				usedDevice = true;
				setInputPath("device");
				yield return null;
			}

			if (NeedsPanelFallback(element)) {
				SendPanelClick(element);
				setInputPath(usedDevice ? "device" : "panel");
				yield return null;
			} else if (!usedDevice) {
				fail("No mouse device is available and the panel fallback could not address the element.");
			}
		}

		public static IEnumerator SetValue(VisualElement element, string value, System.Action<string> setInputPath, System.Action<string> fail) {
			yield return Click(element, setInputPath, fail);

			var keyboard = Keyboard.current;
			if (keyboard != null && !string.IsNullOrEmpty(value)) {
				foreach (char c in value) {
					InputSystem.QueueTextEvent(keyboard, c);
					InputSystem.Update();
				}
				setInputPath("device");
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
			Vector2 localPoint = element.WorldToLocal(element.worldBound.center);
			var nativeDown = new Event {
				type = EventType.MouseDown,
				mousePosition = localPoint,
				button = 0
			};
			using (var down = PointerDownEvent.GetPooled(nativeDown)) {
				down.target = element;
				element.SendEvent(down);
			}
			var nativeUp = new Event {
				type = EventType.MouseUp,
				mousePosition = localPoint,
				button = 0
			};
			using (var up = PointerUpEvent.GetPooled(nativeUp)) {
				up.target = element;
				element.SendEvent(up);
			}
		}
	}
}
