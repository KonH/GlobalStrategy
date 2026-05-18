# Plan 28 — Card Play Animation Flow

## Goal

Rework the card-play sequence from a simple fade-in overlay into a full 15-step animated flow: card slides from hand to test view, result plays, card returns to deck, new card draws from deck to hand. Input is locked for the entire sequence. No code changes touch `src/`.

## Approach

Three existing files change (`OrgActionsView.cs`, `OrgInfoDocument.cs`, `CardPlayAnimator.cs`) and one new file is added (`CardTransitionView.cs`). Two UXML changes are needed in `HUD.uxml` (add transition overlay, fix card-test-card size). No new asmdef or prefab.

The click path changes: instead of `OrgInfoDocument.OnActionCardClicked` pushing `PlayActionCommand` directly, it calls a new `CardPlayAnimator.StartCardPlay(orgId, actionId, cardElement)` entry point. The animator owns the full sequence and pushes the command itself at the right moment.

---

## Steps

### 1 — Update `HUD.uxml`: add card-transition overlay and fix card-test-card size

**File:** `Assets/UI/HUD/HUD.uxml`

Two changes inside `hud-root`:

**1a. Fix card-test-card size** — change the inline style on `card-test-card` from `width: 180px; min-height: 240px` to `width: 240px; min-height: 320px` to match the hand card size defined in `.action-card` (OrgActions.uss).

**1b. Add transition overlay** — insert a new `VisualElement` named `card-transition-overlay` as the last child of `hud-root` (rendered on top of everything, including `card-test-overlay`). It must be:
- `position: Absolute`
- `left: 0; top: 0; right: 0; bottom: 0` (full screen)
- `picking-mode="Ignore"` — no pointer blocking (will be set recursively in C# via helper after card copy is added)
- `display: Flex` always — the overlay itself is always present; only its children appear/disappear

```xml
<ui:VisualElement name="card-transition-overlay" picking-mode="Ignore"
    style="position: absolute; left: 0; top: 0; right: 0; bottom: 0;" />
```

No USS class needed — all styling is inline or on the card copy which reuses `.action-card`. `OrgActions.uss` is already imported at the document level in `HUD.uxml` (line 8), so `.action-card` classes resolve correctly for C#-created elements in this document.

---

### 2 — Create `CardTransitionView.cs`

**File:** `Assets/Scripts/Unity/UI/CardTransitionView.cs`

Plain C# class (no MonoBehaviour). Owns one card-copy `VisualElement` that lives inside the transition overlay.

**Constructor:**
```csharp
CardTransitionView(VisualElement overlay, MonoBehaviour coroutineHost)
```
Stores `overlay` and `coroutineHost`. No card element is created yet.

**Public API:**

```csharp
void Show(
    string actionId,
    Rect fromRect,
    VisualElement toElement,
    float duration,
    ActionConfig actionConfig,
    ActionVisualConfig visualConfig,
    ILocalization loc,
    Action onComplete)
```
- Clears any previous card copy from the overlay.
- Builds a new card copy `VisualElement` with classes `action-card action-card--available` and the same name/image content as hand cards (name label + image, no price labels or tooltip).
- Sets the card copy to `position: Absolute`, `width: 240px`, `height: 320px`.
- Sets initial position to `fromRect` (left/top in overlay-local coords via `overlay.WorldToLocal`).
- Adds the card copy to the overlay.
- Calls `SetPickingIgnoreRecursive` on the card copy.
- Registers a `GeometryChangedEvent` on the card copy (one-shot — unregisters itself on first fire). Inside the callback: reads `toElement.worldBound` to get `toRect` (valid now that layout has run), then starts the lerp coroutine on `coroutineHost`.
- The coroutine lerps `style.left` and `style.top` from `fromRect` position to `toRect` position over `duration` seconds, then calls `onComplete`.

**Note on `toElement`:** Passing the target as a `VisualElement` (not a pre-snapped `Rect`) defers the `worldBound` read until after the `GeometryChangedEvent` fires on the card copy, guaranteeing layout has run for the whole document — including any elements (like `card-test-card`) that just became visible. This avoids the `Rect.zero` bug from reading `worldBound` immediately after `display:None → Flex`.

```csharp
void Hide()
```
- Removes the card copy from the overlay. Safe to call when no card copy exists.

**Private helpers:**
```csharp
static void SetPickingIgnoreRecursive(VisualElement el)
```
Sets `el.pickingMode = PickingMode.Ignore` then recurses into all children.

The coroutine is a private method started via `coroutineHost.StartCoroutine(...)`. It receives fromPos, toPos, duration, and onComplete as parameters (no captured mutable state).

---

### 3 — Update `OrgActionsView.cs`: expose VisualElement in callback, add accessors, add SuppressRefresh

**File:** `Assets/Scripts/Unity/UI/OrgActionsView.cs`

**3a. Change callback signature:**
```csharp
// Before:
public Action<string> OnCardClicked;

// After:
public Action<string, VisualElement> OnCardClicked;
```

In `BuildHandCard`, the `PointerUpEvent` callback changes to:
```csharp
OnCardClicked?.Invoke(capturedId, cardEl);
```
(Pass `cardEl` — the inner card element, not the wrapper — so the caller can read its `worldBound`.)

**3b. Expose deck pile element and hand container:**
```csharp
public VisualElement DeckPileElement { get; private set; }
public VisualElement HandContainer => _handContainer;
```

In `BuildDeckPile`, store a reference to the topmost `front` card element:
```csharp
DeckPileElement = front;
```
`DeckPileElement` is refreshed on every `Refresh()` call since `Refresh` calls `BuildDeckPile` which reassigns it. **Callers must capture `DeckPileElement.worldBound` into a local `Rect` before any operation that triggers a `Refresh()`** (e.g. before pushing a command that changes state).

**3c. Add `SuppressRefresh` flag:**
```csharp
public bool SuppressRefresh { get; set; }
```

Guard the body of `Refresh()`:
```csharp
public void Refresh(OrgActionsState state, CountryResourcesState resources) {
    if (SuppressRefresh) { return; }
    // ... existing rebuild logic ...
}
```

This allows the animator to hold the hand layout stable while a new card is animating in (steps 11–13), preventing `opacity=0` from being reset by a state-triggered rebuild.

---

### 4 — Update `OrgInfoDocument.cs`: inject `CardPlayAnimator`, fix click handler, fix toggle buttons

**File:** `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`

**4a. Add field:**
```csharp
CardPlayAnimator _cardPlayAnimator;
```

**4b. Extend `[Inject]` method** to accept `CardPlayAnimator`:
```csharp
[Inject]
void Construct(..., CardPlayAnimator cardPlayAnimator) {
    ...
    _cardPlayAnimator = cardPlayAnimator;
}
```

**4c. Change `OnActionCardClicked`:**
```csharp
void OnActionCardClicked(string actionId, VisualElement cardElement) {
    if (_cardPlayAnimator == null || _state == null || !_state.PlayerOrganization.IsValid) { return; }
    _cardPlayAnimator.StartCardPlay(_state.PlayerOrganization.OrgId, actionId, cardElement);
}
```
Remove the direct `_commands.Push(new PlayActionCommand {...})` call. If `_commands` is no longer used elsewhere in `OrgInfoDocument`, remove it from the `[Inject]` signature and field declaration.

**4d. Fix toggle button handlers** — `Button.clicked` is non-functional in Unity 6000.4.1f1. Replace with `PointerUpEvent`:
```csharp
// Before:
_charsToggleBtn.clicked += ToggleChars;
_actionsToggleBtn.clicked += ToggleActions;

// After:
_charsToggleBtn.RegisterCallback<PointerUpEvent>(e => {
    if (e.button == 0 && _charsToggleBtn.ContainsPoint(e.localPosition)) { ToggleChars(); }
});
_actionsToggleBtn.RegisterCallback<PointerUpEvent>(e => {
    if (e.button == 0 && _actionsToggleBtn.ContainsPoint(e.localPosition)) { ToggleActions(); }
});
```

---

### 5 — Rework `CardPlayAnimator.cs`: implement the 15-step flow

**File:** `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`

**5a. Add fields:**
```csharp
CardTransitionView _transitionView;
OrgActionsView _actionsView;
bool _resultReady;
```

**5b. Replace `HandleLastActionChanged` body entirely.** The old body started `PlaySequence` from the event — remove that. The new body only signals the waiting coroutine:
```csharp
void HandleLastActionChanged(object sender, PropertyChangedEventArgs e) {
    if (_state != null && _state.LastAction.HasResult) {
        _resultReady = true;
    }
}
```
Keep the `OnEnable`/`OnDisable` subscription unchanged.

**5c. Add public entry point:**
```csharp
public void StartCardPlay(string orgId, string actionId, VisualElement clickedCard)
```
- Guards: if `_isPlaying` return immediately.
- Starts coroutine `PlaySequence(orgId, actionId, clickedCard)`.

**5d. Add `SetActionsView(OrgActionsView view)` method** called from `OrgInfoDocument.InitViews`:
```csharp
public void SetActionsView(OrgActionsView view) {
    _actionsView = view;
}
```

**5e. Rewrite `PlaySequence` coroutine** implementing all 15 steps:

```
Step 1  — Lock input: _isPlaying = true. ModalState.IsModalOpen = true.
          _resultReady = false.
          _commands.Push(new PauseCommand()).

Step 2  — Show card-test-overlay (DisplayStyle.Flex, opacity 0).
          Populate card-test-card slot with card content (PopulateTestCard).
          Keep card-test-card opacity 0.

Step 3  — Capture fromRect = clickedCard.worldBound (valid: card is visible in hand).
          Call _transitionView.Show(actionId, fromRect, toElement: cardTestCard, 0.77f, ..., onComplete: () => _transitionDone = true).
          (cardTestCard.worldBound is read inside GeometryChangedEvent after layout — not here.)

Step 4  — (No explicit card hiding needed. The card copy in the overlay provides visual
          coverage. The actual hand card will be replaced by Refresh() naturally.)

Step 5  — Capture deckRect = _actionsView.DeckPileElement?.worldBound ?? Rect.zero.
          (Captured now, before any state change triggers a Refresh() that replaces DeckPileElement.)

Step 6  — Wait for _transitionDone (poll with yield return null each frame).
          When done: show card-test-overlay and card-test-card (opacity 1).
          _transitionView.Hide().

Step 7  — Push PlayActionCommand { OrgId, ActionId }.
          Show roll-block (DisplayStyle.Flex, opacity 0 → 1).
          Animate dice roll for 2s (existing logic: random number cycling, then success/fail label).
          Wait for _resultReady (poll with yield return null each frame).
          Read success = _state.LastAction.Success.
          Read discoveredCountryId = _state.DiscoveredCountries.RecentlyDiscovered.

Step 8  — _transitionDone = false.
          Call _transitionView.Show(actionId, fromRect: cardTestCard.worldBound,
            toElement: _actionsView.DeckPileElement (read worldBound inside GeometryChangedEvent),
            0.77f, ..., onComplete: () => _transitionDone = true).
          Note: use deckRect captured in step 5 as a fallback if DeckPileElement is null;
          alternatively pass DeckPileElement directly so Show reads its worldBound after layout.

Step 9  — Hide card-test-overlay (DisplayStyle.None).
          Hide roll-block (DisplayStyle.None).

Step 10 — Wait for _transitionDone (poll each frame).
          _transitionView.Hide().

Step 11 — Wait one additional frame (yield return null) for state to propagate and
          OrgActionsView.Refresh() to rebuild the hand with the new card.
          _actionsView.SuppressRefresh = true.
          Find the new hand card: query _actionsView.HandContainer children,
          locate the last card element (newly drawn card is always appended at end).
          Set newHandCard.style.opacity = 0.

Step 12 — _transitionDone = false.
          Call _transitionView.Show(actionId (new card's id), fromRect: deckRect,
            toElement: newHandCard, 0.5f, ..., onComplete: () => _transitionDone = true).

Step 13 — Wait for _transitionDone.
          newHandCard.style.opacity = 1.
          _transitionView.Hide().
          _actionsView.SuppressRefresh = false.

Step 14 — Only if success and discoveredCountryId not empty:
          _cameraController.PanToCountry(discoveredCountryId).
          Show and animate fly-text (existing logic, unchanged).

Step 15 — _state.DiscoveredCountries.ClearRecentlyDiscovered().
          _state.LastAction.Clear().
          ModalState.IsModalOpen = false.
          _commands.Push(new UnpauseCommand()).
          _isPlaying = false.
```

**Implementation notes:**

- **`_transitionDone` flag:** local `bool` in the coroutine; set false before each `Show`, set true in the `onComplete` callback. Poll with `while (!_transitionDone) yield return null`.
- **`_resultReady` flag:** set by `HandleLastActionChanged` (step 5b). Reset to `false` at start of PlaySequence (step 1). Poll with `while (!_resultReady) yield return null` in step 7.
- **Step 11 — identifying the new card:** After `Refresh()` rebuilds the hand, the deck draw always appends the new card at the end of `HandContainer`. Query `HandContainer` children by index (last child's inner `.action-card` element).
- **Steps 11/12 — new card action id:** obtain from `_state.PlayerOrgActions.Hand` last entry after result arrives.

**5f. `PopulateTestCard`** — no change needed.

---

### 6 — Wire `CardPlayAnimator` reference into `OrgInfoDocument`

**File:** `Assets/Scripts/Unity/UI/OrgInfoDocument.cs` (continued from step 4)

In `InitViews`, after creating `_actionsView`, call:
```csharp
_cardPlayAnimator?.SetActionsView(_actionsView);
```

---

### 7 — Instantiate `CardTransitionView` in `CardPlayAnimator.Awake`

**File:** `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`

In `Awake`, after getting `_hudDocument`:
```csharp
var overlay = _hudDocument.rootVisualElement.Q("card-transition-overlay");
_transitionView = new CardTransitionView(overlay, this);
```

---

### 8 — GameLifetimeScope registration

**File:** `Assets/Scripts/Unity/DI/GameLifetimeScope.cs`

`CardPlayAnimator` is already registered:
```csharp
builder.RegisterComponentInHierarchy<CardPlayAnimator>();
```
No change needed. `OrgInfoDocument` receives it via its `[Inject]` method automatically.

---

## Key constraints (from project rules)

- Use `PointerUpEvent` with manual `ContainsPoint` check — never `Button.clicked`.
- `worldBound` is zero on newly added or just-made-visible elements — `CardTransitionView.Show` accepts `toElement: VisualElement` and reads `worldBound` inside `GeometryChangedEvent`.
- `PickingMode.Ignore` is not recursive — apply `SetPickingIgnoreRecursive` to the card copy after adding it to the overlay.
- No Canvas/UGUI — all animation via `style.left` / `style.top` / `style.opacity`.
- Coroutines on `CardPlayAnimator` (MonoBehaviour) — `CardTransitionView` delegates `StartCoroutine` to the host.
- Tabs, opening brace on same line, `_` prefix, no explicit `private`, always use braces.

---

Use /implement to start working on the plan or request changes.
