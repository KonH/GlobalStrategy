# Plan 17 — Tooltip Layers

## Goal

Replace the single-level `TooltipController` with a multi-level `TooltipSystem` that supports:
- Auto-pin after 2 seconds of display
- Manual pin via middle mouse button
- Unlimited nested inner tooltips (underlined triggers inside tooltip content)
- Cycle prevention (no re-opening an ancestor tooltip)
- Correct hide rules (non-pinned hides on mouse-leave; pinned hides on click-outside)

Apply the new system to the resource counter: the main tooltip shows aggregated +/- monthly values; hovering each value reveals an inner tooltip listing individual effects.

---

## Approach

### TooltipSystem (replaces TooltipController)

Plain C# class. Keeps a `List<TooltipEntry>` stack, one entry per open level.

**`TooltipEntry`** fields:
- `VisualElement Panel` — absolutely-positioned overlay appended to `hudRoot`
- `string Id` — unique string for cycle detection
- `HashSet<string> Ancestors` — IDs of all ancestor tooltips
- `bool IsPinned`
- `float ElapsedSeconds` — time since shown (incremented in `Update`)
- `bool IsPointerOverPanel`

**Constructor**: `TooltipSystem(VisualElement hudRoot)` — stores root, no panels created yet.

**`RegisterTrigger(VisualElement trigger, string id, Func<TooltipContext, VisualElement> buildContent, HashSet<string> ancestors)`**

- If `ancestors.Contains(id)`: skip registration entirely (no underline, no hover).
- `PointerEnterEvent` on trigger:
  - Close any stack entries opened after the current nesting level (i.e. close inner tooltips of peer triggers).
  - Create a new panel `VisualElement`, append to `hudRoot`.
  - Call `buildContent(context)` where `context` lets content register inner triggers.
  - Push new `TooltipEntry` onto stack.
- `PointerLeaveEvent` on trigger: call `TryHideTop()` if not pinned and pointer not over panel.
- `PointerEnterEvent`/`PointerLeaveEvent` on panel: update `IsPointerOverPanel`.

**`Update(float deltaTime)`** (called from `HUDDocument.Update`):
- Increment `ElapsedSeconds` for each unpinned entry; auto-pin when ≥ 2s.
- Check `Mouse.current.middleButton.wasPressedThisFrame`: pin the topmost entry whose panel contains the pointer.
- Check `Mouse.current.leftButton.wasPressedThisFrame`:
  - If any entry is pinned and the click position is outside **all** open tooltip panels, hide all entries from the topmost pinned one outward.

**`TryHideTop()`**: hide and remove the top stack entry if it is not pinned and `!IsPointerOverPanel`.

**Panel creation**: each panel is a `VisualElement` with class `tooltip-overlay` (reuses existing USS), positioned absolutely. Positioned via `worldBound` of the trigger, same as the current `PositionNear`.

### TooltipContext

Passed to the `buildContent` callback. Holds the ancestor set (`entry.Ancestors ∪ {entry.Id}`) and a reference to `TooltipSystem`. Exposes:

```csharp
void RegisterInnerTrigger(VisualElement trigger, string id, Func<TooltipContext, VisualElement> buildContent)
```

This calls back into `TooltipSystem.RegisterTrigger` with the expanded ancestor set.

### ResourcesView update

`BuildResourceTooltip` changes:
- Header: resource name (unchanged).
- Aggregate positive monthly effects into `plusTotal`; negative into `minusTotal`.
- Render a `plusRow` label: `"+{plusTotal:F1}/month"` styled `tooltip-effect-positive`.
  - If there are positive effects: mark as inner-trigger (underline class); register inner trigger with id `"{resourceId}.plus"` → builds effect list for positive effects only.
- Render a `minusRow` label similarly for negatives.
- Instant effects (if any): single row `"{sum} instant"` with its own inner trigger.
- Remove the existing inline `PointerEnter/Leave` description toggle from effect rows — descriptions move into the inner tooltip content.

Inner effect list content (second level):
- Header matching the sign row text.
- One row per effect: `"{effectName}: {sign}{value:F1}/month"` + description below (plain text, no further nesting).

### HUDDocument changes

- `Awake`: replace `new TooltipController(root.Q("tooltip-overlay"))` with `new TooltipSystem(root.Q("hud-root"))`.
- Add `void Update() { _tooltip.Update(Time.deltaTime); }`.

### UXML change

- Remove `<ui:VisualElement name="tooltip-overlay" class="tooltip-overlay" />` from `HUD.uxml` — the system creates panels dynamically.

### USS additions (HUD.uss)

```css
.tooltip-inner-trigger {
    border-bottom-width: 1px;
    border-color: rgb(75, 50, 15);
    cursor: link;
}
```

Apply this class to inner-trigger labels in C# after registration.

---

## Steps

1. **Rewrite `TooltipController.cs`** as `TooltipSystem` — new class, same file.
   - Constructor takes `VisualElement hudRoot`.
   - Implement stack, `RegisterTrigger`, `Update`, `TryHideTop`, panel creation.

2. **Add `TooltipContext.cs`** — plain C# class holding ancestor set + system reference, exposes `RegisterInnerTrigger`.

3. **Update `HUDDocument.cs`**:
   - Change tooltip construction to `new TooltipSystem(root.Q("hud-root"))`.
   - Add `Update()` calling `_tooltip.Update(Time.deltaTime)`.
   - Store tooltip as `TooltipSystem _tooltip` field.

4. **Update `ResourcesView.cs`**:
   - Change constructor parameter from `TooltipController` to `TooltipSystem`.
   - Change `RegisterTooltip` call to `RegisterTrigger` with new signature.
   - `BuildResourceTooltip` — aggregate effects, add `plusRow`/`minusRow` inner triggers via `TooltipContext`.
   - Remove inline description expand/collapse.

5. **Update `PlayerCountryView.cs` and `CountryInfoView.cs`** — parameter type change only (`TooltipSystem`).

6. **Update `HUD.uxml`** — remove static `tooltip-overlay` element.

7. **Update `HUD.uss`** — add `.tooltip-inner-trigger` style.

---

Use /implement to start working on the plan or request changes.
