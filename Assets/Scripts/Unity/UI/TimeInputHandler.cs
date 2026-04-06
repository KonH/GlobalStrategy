using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using GS.Main;
using GS.Game.Commands;

namespace GS.Unity.UI {
	public class TimeInputHandler : MonoBehaviour {
		IWriteOnlyCommandAccessor _commands;
		TimeState _time;

		[Inject]
		void Construct(IWriteOnlyCommandAccessor commands, VisualState state) {
			_commands = commands;
			_time = state.Time;
		}

		void Update() {
			var keyboard = Keyboard.current;
			if (keyboard == null) return;
			if (keyboard.spaceKey.wasPressedThisFrame) {
				if (_time.IsPaused)
					_commands.Push(new UnpauseCommand());
				else
					_commands.Push(new PauseCommand());
			}
			if (keyboard.digit1Key.wasPressedThisFrame) _commands.Push(new ChangeTimeMultiplierCommand(0));
			if (keyboard.digit2Key.wasPressedThisFrame) _commands.Push(new ChangeTimeMultiplierCommand(1));
			if (keyboard.digit3Key.wasPressedThisFrame) _commands.Push(new ChangeTimeMultiplierCommand(2));
		}
	}
}
