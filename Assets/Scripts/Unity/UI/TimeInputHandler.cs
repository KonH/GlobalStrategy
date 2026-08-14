using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using GS.Main;
using GS.Game.Commands;
using GS.Unity.Common;

namespace GS.Unity.UI {
	public class TimeInputHandler : MonoBehaviour {
		IWriteOnlyCommandAccessor _commands;
		TimeState _time;
		ModalState _modalState;
		TutorialPresentationTriggers _presentationTriggers;

		[Inject]
		void Construct(
			IWriteOnlyCommandAccessor commands,
			VisualState state,
			ModalState modalState,
			TutorialPresentationTriggers presentationTriggers) {
			_commands = commands;
			_time = state.Time;
			_modalState = modalState;
			_presentationTriggers = presentationTriggers;
		}

		void Update() {
			var keyboard = Keyboard.current;
			if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) {
				_presentationTriggers?.Set(TutorialPresentationTriggers.KeyPressed, 1);
			}
			if (AnyPointerPressedThisFrame()) {
				_presentationTriggers?.Set(TutorialPresentationTriggers.KeyPressed, 1);
			}
			if (_modalState != null && _modalState.IsLocked()) {
				return;
			}
			if (keyboard == null) {
				return;
			}
			if (keyboard.spaceKey.wasPressedThisFrame) {
				if (_time.IsPaused) {
					_commands.Push(new UnpauseCommand());
				} else {
					_commands.Push(new PauseCommand());
				}
			}
			if (keyboard.digit1Key.wasPressedThisFrame) {
				_commands.Push(new ChangeTimeMultiplierCommand(0));
			}
			if (keyboard.digit2Key.wasPressedThisFrame) {
				_commands.Push(new ChangeTimeMultiplierCommand(1));
			}
			if (keyboard.digit3Key.wasPressedThisFrame) {
				_commands.Push(new ChangeTimeMultiplierCommand(2));
			}
		}

		// "Any key" for tutorial purposes also covers mouse buttons and touch/gesture presses -
		// not just the physical keyboard - so players on trackpad/touch devices (or who simply
		// click instead of typing) can still advance a keyPressed-gated tutorial step.
		static bool AnyPointerPressedThisFrame() {
			var mouse = Mouse.current;
			if (mouse != null && (
				mouse.leftButton.wasPressedThisFrame ||
				mouse.rightButton.wasPressedThisFrame ||
				mouse.middleButton.wasPressedThisFrame)) {
				return true;
			}
			var touchscreen = Touchscreen.current;
			if (touchscreen != null) {
				foreach (var touch in touchscreen.touches) {
					if (touch.press.wasPressedThisFrame) {
						return true;
					}
				}
			}
			return false;
		}
	}
}
