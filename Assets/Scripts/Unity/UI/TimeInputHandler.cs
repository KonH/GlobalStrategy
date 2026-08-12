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

		[Inject]
		void Construct(IWriteOnlyCommandAccessor commands, VisualState state, ModalState modalState) {
			_commands = commands;
			_time = state.Time;
			_modalState = modalState;
		}

		void Update() {
			if (_modalState != null && _modalState.IsLocked()) {
				return;
			}
			var keyboard = Keyboard.current;
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
	}
}
