# Game Loop Integration from UI

## Command ordering: action before pause

When a UI sequence needs to both execute a game action and pause the game, push **both commands in the same frame** — action first, pause second:

```csharp
_commands.Push(new PlayActionCommand { OwnerId = orgId, ActionId = actionId });
_commands.Push(new PauseCommand());
```

`GameLogic.Update()` drains the entire command buffer in one tick. Both commands are processed before the pause takes effect, so the action is guaranteed to be handled.

If you push `PauseCommand` first (or in a prior frame) and `PlayActionCommand` later, the game is already paused when the action command arrives — `GameLogic.Update()` skips processing while paused, the command is never executed, and any result-ready signal (`_resultReady`) never fires.

## Waiting for async results from a coroutine

After pushing a state-changing command, poll a flag that `PropertyChanged` sets:

```csharp
bool _resultReady;

void HandleLastActionChanged(object sender, PropertyChangedEventArgs e) {
    if (_state != null && _state.LastAction.HasResult) {
        _resultReady = true;
    }
}

// In the coroutine:
_resultReady = false;
_commands.Push(new PlayActionCommand { ... });
float startTime = Time.time;
while (!_resultReady) {
    yield return new WaitForSeconds(0.33f);
    if (Time.time - startTime > 10f) {
        Debug.LogWarning("Timed out waiting for action result.");
        break;
    }
}
```

Always include a timeout — if the command is never processed (e.g. due to ordering bugs), the coroutine would otherwise hang forever.
