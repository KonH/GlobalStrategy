# 08 — Time Feature

## Goal

Add a game clock that advances in hours, with pause and three speed settings, driven by the existing ECS + commands + VisualState pipeline and displayed in the HUD top-right.

---

## Architecture Overview

```
game_settings.json → GameSettings config
PauseCommand / UnpauseCommand / ChangeTimeMultiplierCommand
TimeSystem (pure C#, advances GameTime singleton)
VisualStateConverter → TimeState (INotifyPropertyChanged)
HUDDocument → TimeView (plain C#) → UXML time block
InputHandler → Push commands on Space / 1 / 2 / 3
```

Speed multipliers (hours advanced per real second):

| Button | Multiplier index | Hours/sec |
|--------|-----------------|-----------|
| x1     | 0               | 1         |
| x2     | 1               | 24        |
| x3     | 2               | 720       |

---

## Steps

### 1 — Game.Configs: `GameSettings`

Add `src/Game.Configs/GameSettings.cs`:

```csharp
namespace GS.Game.Configs {
    public class GameSettings {
        public int StartYear { get; set; } = 1880;
        public int[] SpeedMultipliers { get; set; } = { 1, 24, 720 };
    }
}
```

Add `game_settings.json` to `Assets/StreamingAssets/Configs/`:

```json
{
  "startYear": 1880,
  "speedMultipliers": [1, 24, 720]
}
```

---

### 2 — Game.Commands: time commands

Add to `src/Game.Commands/`:

```csharp
record struct PauseCommand() : ICommand;
record struct UnpauseCommand() : ICommand;
record struct ChangeTimeMultiplierCommand(int Index) : ICommand;
```

---

### 3 — Game.Components: `GameTime` singleton

Add `src/Game.Components/GameTime.cs`:

```csharp
using System;

namespace GS.Game.Components {
    public struct GameTime {
        public DateTime CurrentTime;
        public bool IsPaused;
        public int MultiplierIndex;
    }
}
```

---

### 4 — Game.Systems: `TimeSystem`

Add `src/Game.Systems/TimeSystem.cs`:

```csharp
static class TimeSystem {
    public static void Update(
        World world,
        float deltaTime,
        int[] speedMultipliers,
        ReadCommands<PauseCommand> pause,
        ReadCommands<UnpauseCommand> unpause,
        ReadCommands<ChangeTimeMultiplierCommand> changeSpeed)
    {
        ref var time = ref world.GetSingleton<GameTime>();
        if (pause.Count > 0) time.IsPaused = true;
        if (unpause.Count > 0) time.IsPaused = false;
        changeSpeed.ForEach(cmd => time.MultiplierIndex = cmd.Index);
        if (!time.IsPaused) {
            int hours = (int)(deltaTime * speedMultipliers[time.MultiplierIndex]);
            time.CurrentTime = time.CurrentTime.AddHours(hours);
        }
    }
}
```

---

### 5 — Game.Main: `TimeState` + wiring

**`TimeState.cs`** (new, in `Game.Main`):

```csharp
public class TimeState : INotifyPropertyChanged {
    public event PropertyChangedEventHandler? PropertyChanged;
    public DateTime CurrentTime { get; private set; }
    public bool IsPaused { get; private set; }
    public int MultiplierIndex { get; private set; }

    public void Set(DateTime time, bool paused, int multiplierIndex) {
        CurrentTime = time;
        IsPaused = paused;
        MultiplierIndex = multiplierIndex;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
```

**`VisualState.cs`** — add `TimeState Time { get; } = new();`

**`VisualStateConverter`** — query `GameTime` singleton, call `_state.Time.Set(...)`.

**`GameLogicContext`** — add `IConfigSource<GameSettings> GameSettings`.

**`GameLogic`**:
- Load `GameSettings` in constructor; create `GameTime` singleton entity with `CurrentTime = new DateTime(settings.StartYear, 1, 1)`, `IsPaused = false`, `MultiplierIndex = 0`.
- Store `settings.SpeedMultipliers` for use in `Update`.
- In `Update`: call `TimeSystem.Update(...)` before `SelectCountrySystem`.

---

### 6 — Rebuild DLLs

```
dotnet build src/GlobalStrategy.Core.sln -c Release
```

Refresh Unity; verify no compilation errors.

---

### 7 — HUD: time block UXML/USS

**`Assets/UI/HUD/Time/Time.uxml`** (template):

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Style src="project://database/Assets/UI/HUD/Time/Time.uss" />
    <ui:VisualElement name="time-root">
        <ui:Label name="time-date" />
        <ui:VisualElement name="time-controls">
            <ui:Button name="btn-pause" text="⏸" />
            <ui:Button name="btn-x1"   text="x1" />
            <ui:Button name="btn-x2"   text="x2" />
            <ui:Button name="btn-x3"   text="x3" />
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

**`Assets/UI/HUD/Time/Time.uss`** — internal styles (font size, button size, row layout).

**`Assets/UI/HUD/HUD.uxml`** — add `<ui:Template>` + `<ui:Instance class="time-panel">` inside `hud-root`.

**`Assets/UI/HUD/HUD.uss`** — add `.time-panel` rule: `position: absolute; top: 0; right: 0;`.

---

### 8 — `TimeView.cs`

Plain C# class in `Assets/Scripts/Unity/UI/`:

```csharp
class TimeView {
    readonly VisualElement _root;
    readonly Label _date;
    readonly Button _btnPause, _btnX1, _btnX2, _btnX3;
    Action _onPauseToggle;
    Action<int> _onSpeedChange;

    public TimeView(VisualElement root, Action onPauseToggle, Action<int> onSpeedChange) {
        _root = root;
        _date = root.Q<Label>("time-date");
        _btnPause = root.Q<Button>("btn-pause");
        _btnX1 = root.Q<Button>("btn-x1");
        _btnX2 = root.Q<Button>("btn-x2");
        _btnX3 = root.Q<Button>("btn-x3");
        _onPauseToggle = onPauseToggle;
        _onSpeedChange = onSpeedChange;
        _btnPause.clicked += () => _onPauseToggle();
        _btnX1.clicked += () => _onSpeedChange(0);
        _btnX2.clicked += () => _onSpeedChange(1);
        _btnX3.clicked += () => _onSpeedChange(2);
    }

    public void Refresh(TimeState state) {
        _date.text = state.CurrentTime.ToString("dd/MM/yyyy");
        _btnPause.text = state.IsPaused ? "▶" : "⏸";
        // highlight active speed button via USS class
        SetActive(_btnX1, state.MultiplierIndex == 0);
        SetActive(_btnX2, state.MultiplierIndex == 1);
        SetActive(_btnX3, state.MultiplierIndex == 2);
    }

    void SetActive(Button btn, bool active) {
        if (active) btn.AddToClassList("active");
        else btn.RemoveFromClassList("active");
    }
}
```

---

### 9 — `HUDDocument.cs`: wire `TimeView`

- In `Awake`: query `"time-panel"` template instance, create `TimeView` with callbacks that push commands via `IWriteOnlyCommandAccessor`.
- In `OnEnable`/`OnDisable`: subscribe/unsubscribe to `VisualState.Time.PropertyChanged`.
- Callback pushes `PauseCommand` or `UnpauseCommand` based on `TimeState.IsPaused`, and `ChangeTimeMultiplierCommand(index)`.

---

### 10 — `TimeInputHandler.cs` (MonoBehaviour)

New script on the `GameLifetimeScope` GameObject or dedicated Input GO:

```csharp
class TimeInputHandler : MonoBehaviour {
    [Inject] IWriteOnlyCommandAccessor _commands;

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space))
            // toggle: inspect TimeState.IsPaused, push Pause or Unpause
        if (Input.GetKeyDown(KeyCode.Alpha1)) _commands.Push(new ChangeTimeMultiplierCommand(0));
        if (Input.GetKeyDown(KeyCode.Alpha2)) _commands.Push(new ChangeTimeMultiplierCommand(1));
        if (Input.GetKeyDown(KeyCode.Alpha3)) _commands.Push(new ChangeTimeMultiplierCommand(2));
    }
}
```

`TimeInputHandler` needs access to `TimeState` (injected) to know current pause state before toggling.

Register in `GameLifetimeScope`:
```csharp
builder.RegisterComponentInHierarchy<TimeInputHandler>();
```

---

### 11 — `GameLogicContext` wiring in Unity

In the Unity-side bootstrap (e.g. `GameLifetimeScope` or wherever `GameLogic` is constructed), add:
```csharp
new FileConfig<GameSettings>("Configs/game_settings.json")
```

---

### 12 — Smoke test

- Enter Play Mode.
- Verify date advances at x1 speed.
- Press Space → clock pauses; press again → resumes.
- Press 2 → x2 speed (days/sec); press 3 → x3 (months/sec).
- Click HUD buttons; verify they match keyboard behavior.
