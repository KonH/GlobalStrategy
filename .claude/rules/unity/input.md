# Unity Input Handling

The project uses the **Input System package** (`com.unity.inputsystem`). The legacy `UnityEngine.Input` class is disabled in Player Settings and throws `InvalidOperationException` at runtime — never use it.

## Assembly Reference

Any asmdef whose scripts read input must reference the Input System assembly:

```json
"GUID:75469ad4d38634e559750d17036d5f7c"
```

## API

```csharp
using UnityEngine.InputSystem;

var keyboard = Keyboard.current;
if (keyboard == null) return;

keyboard.spaceKey.wasPressedThisFrame
keyboard.digit1Key.wasPressedThisFrame   // number row 1
keyboard.digit2Key.wasPressedThisFrame
keyboard.digit3Key.wasPressedThisFrame

var mouse = Mouse.current;
if (mouse == null) return;

mouse.leftButton.wasPressedThisFrame
mouse.position.ReadValue()               // Vector2 screen position
```

- Always null-check `Keyboard.current` / `Mouse.current` before use — they are null when no device is connected.
- Use `wasPressedThisFrame` for one-shot actions (key down), `isPressed` for held state.
