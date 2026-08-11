# Plan: Org Destroyed Window (UI)

## Spec

Source: `Docs/Specs/26_08_11_09_org-destroy-ui/spec.md` (approved; owner
clarifications baked in — shown for every destruction including the player's
own, `OrgInfoDocument` closes permanently for the player's own destruction, and
`EndGameWindow` must sequence after `OrgDestroyedWindow`).

When Part A marks an org destroyed, show a `CountryDestroyedWindow`-style modal
`OrgDestroyedWindow` (flavor header/body with org name, reused
`country_destroy.png` art, close + confirm) that blocks all UI via
`ModalState`, consumes an independent FIFO queue on `VisualState` (same
`Enqueue`/`TryPeek`/`AcknowledgeCurrent` pattern as `WarResultWindow`/
`CountryDestroyedWindow`). Immediately hide `OrgInfoDocument`, close its
subpanels, reset the HUD open flag, and prevent reopening when the destroyed
org is the player's own. Fix `EndGameWindowDocument` — which today opens
unconditionally and immediately on `GameCompletion.IsCompleted`, ignoring
`ModalState` entirely — to participate in the same "wait your turn" convention
as the FIFO windows, so it opens right after `OrgDestroyedWindow` is
acknowledged instead of stacking on top of it.

**Depends on Part A (domain):** `Docs/Specs/26_08_11_09_org-destroy-logic/`
(spec + plan approved). Part A owns, and this plan only consumes:

| Surface | Name (Part A plan) |
|---|---|
| Persistent flag | `IsOrgDestroyed` on the org entity |
| One-shot ECS event | `OrgDestroyedApplied { OrganizationId }` (swept next tick) |
| `VisualState` FIFO | `VisualState.OrgDestroyedResults` (`OrgDestroyedResultsState`/`OrgDestroyedSnapshotState`, mirrors `CountryDestroyedResults`) |
| Player-org destroyed flag | `VisualState.PlayerOrganization.IsDestroyed` (bool on `PlayerOrganizationState`) |
| Session-ending loss | `VisualState.GameCompletion.IsCompleted`/`Result == GameResult.Lose` (no new field — existing projection already yields `Lose` when `WinnerOrganizationId` is empty) |

This UI plan **does not** recreate the `VisualState` queue, the converter
enqueue, or the `PlayerOrganization.IsDestroyed` projection — Part A's own plan
already covers them (unlike the country-destroy precedent, where Part A's
FIFO/projection work happened to already exist before this feature; here it is
new, but still Part A's responsibility per that plan's §10).

Acceptance criteria (condensed):
- **Modal** — open from FIFO via `TryOpenIfQueued`/`OpenCurrent`;
  `ModalState.Lock(this)` on open; close + confirm both hide visually, then
  `AcknowledgeCurrent()` while the window still owns `ModalState`, then
  `Unlock(this)`.
- **Chrome** — flavor header/body naming the destroyed org (conspiracy-of-other-orgs
  framing), reused `country_destroy.png` image, close + confirm; dark theme;
  UI Toolkit only; shown for every destruction including the player's own.
- **Blocking** — full UI lock like `WarResult`/`CountryDestroyed` (not
  map-only).
- **`OrgInfoDocument`** — immediately hides when the destroyed org is the
  player's own (`PlayerOrganization.IsDestroyed`), closes both subpanels,
  resets `HUDDocument._orgPanelOpen = false`, and cannot reopen. There is no
  gray-out alternative.
- **`EndGameWindow` sequencing** — gate opening on both
  `!ModalState.IsLocked()` and no pending
  `VisualState.OrgDestroyedResults.TryPeek`; subscribe to
  `ModalState.Unlocked`; opens immediately as today only when neither an open
  modal nor an org-destroy notification awaiting its turn blocks it.
- **Locale** — `en.asset`/`ru.asset` keys under `org_destroyed.*`; interpolate
  `organization_name.{OrgId}`.
- **Last-org-standing presentation** — consume Part A's projected
  `WinConditionHintKind.LastOrgStanding` in `SelectOrgDocument` and
  `GoalsWindowView`, with EN/RU text and runtime progress shown as destroyed
  opponents / total opponents.

## Goal

Ship a `CountryDestroyedWindow`-style FIFO notification modal for org
destruction, permanent player-org panel closure, last-org-standing presentation,
and a fix so
`EndGameWindow` no longer stacks on top of any still-open notification window
— without touching Part A's destroy math, `VisualState` queue/projection
creation (already Part A's), or map rendering.

## Approach

### 1. Consume Part A `VisualState` FIFO + flags (no duplicate queue)

Assume Part A exposes (per its own plan §10):

```text
VisualState.OrgDestroyedResults : OrgDestroyedResultsState
  Enqueue(OrgDestroyedSnapshotState) / TryPeek(out ...) / AcknowledgeCurrent()
  Entries / PropertyChanged  // same contract as CountryDestroyedResultsState

VisualState.PlayerOrganization.IsDestroyed : bool
```

`OrgDestroyedSnapshotState` needs at least `OrganizationId` for binding. UI
document self-drives from this queue (`CountryDestroyedWindow` style) — no HUD
click forwarder, no pause/`ShouldPause`, no Events notification config.

**Modal coexistence:** `OrgDestroyedResults` is independent of `WarResults`/
`CountryDestroyedResults`. Keep the existing per-type FIFOs plus shared
`ModalState`; do not add a global/cross-window queue. Part A guarantees the
org-destroy FIFO publishes before completion in the same converter pass; this
plan consumes that order and gates on both lock state and pending org work.
This second guard is required when another modal already owns `ModalState`:
after it unlocks, `EndGameWindowDocument` and `OrgDestroyedWindowDocument`
subscribers may run in either order, but EndGame still sees the pending org
snapshot and yields. `OrgDestroyedWindow` does not pause or unpause.

### 2. UXML / USS / Document / View — follow `CountryDestroyedWindow`

| Asset / type | Path |
|---|---|
| UXML / USS | `Assets/UI/Modal/OrgDestroyedWindow/OrgDestroyedWindow.uxml` + `.uss` |
| Document | `Assets/Scripts/Unity/UI/OrgDestroyedWindowDocument.cs` |
| View | `Assets/Scripts/Unity/UI/OrgDestroyedWindowView.cs` |

Use `CountryDestroyedWindowDocument.cs`/`CountryDestroyedWindowView.cs`/
`.uxml`/`.uss` as the structural baseline, renaming `country-destroyed-*` → `org-destroyed-*`,
`CountryDestroyedWindowDocument/View` → `OrgDestroyedWindowDocument/View`,
`country_destroyed.*` locale keys → `org_destroyed.*`, `CountryDestroyedResults`
→ `OrgDestroyedResults`, `CountryDestroyedSnapshotState` →
`OrgDestroyedSnapshotState`. Drop `DeselectIfDestroyedSelected` and its
`WorldCountries.PropertyChanged` subscription entirely — there is no
"selected org" concept analogous to `SelectedCountryState` (per spec §6
resolution, the only player-facing consequence of the player's own org being
destroyed is the `OrgInfoDocument` change in §4 below, not a selection-clear).

**Document behaviour** (copy `CountryDestroyedWindowDocument` lifecycle,
drop deselect):
- `sortingOrder = 515` — same band as `CountryDestroyedWindow`; both sit below
  `EndGameWindow` (1100).
- `HideVisualOnly()` on `Awake` (no unlock/acknowledge); `ModalState.Lock(this)`
  in `OpenCurrent`. For user dismissal, `Hide()` must perform this exact order:
  hide the root, `AcknowledgeCurrent()` **while this document still owns the
  modal lock**, then `_modalState.Unlock(this)`. Do not clone the current
  country-window unlock-before-ack order. Acknowledge-before-unlock ensures all
  `Unlocked` subscribers observe the post-dismissal queue: EndGame opens after
  the final org notification, while another queued org notification gets the
  next turn without reopening the just-acknowledged snapshot.
- Subscribe to `OrgDestroyedResults.PropertyChanged` + `Locale`; `Start()` and
  `HandleModalUnlocked` both call `TryOpenIfQueued()`.
- Close/confirm: `PointerUpEvent` + `ContainsPoint` (Unity 6000.4.1 hit-test
  quirk, matches `CountryDestroyedWindowDocument`), both call the same
  user-dismiss `Hide`.
- Inject: `VisualState`, `ILocalization`, `ModalState` (no
  `IWriteOnlyCommandAccessor` needed — no deselect command to push).
- Register `OrgDestroyedWindowDocument` in `GameLifetimeScope` via
  `RegisterComponentInHierarchy`, immediately after
  `CountryDestroyedWindowDocument` (`GameLifetimeScope.cs:115`) and before
  `EndGameWindowDocument` (`GameLifetimeScope.cs:120`).

**View:** `GetOrgName(orgId)` — copy `WarResultWindowView.GetOrgName`'s exact
pattern (`organization_name.{orgId}` lookup, fallback to raw id) into
`OrgDestroyedWindowView` as its own private helper (existing view classes each
keep their own copy rather than sharing one — follow that precedent). Bind
header/body via `string.Format` on locale formats + the org name; set confirm
button label from locale.

### 3. Player org panel — close immediately and permanently

`OrgInfoDocument` is bound solely to `VisualState.PlayerOrganization`. When
`IsDestroyed` changes true, call `Hide()` immediately; that existing method
also calls `SetCharsOpen(false)` and `SetActionsOpen(false)`, closing the
character and action subpanels and their picking/tooltip state. `Show()` must
refuse while the player org is destroyed as a defensive guard.

`HUDDocument.HandlePlayerOrgChanged` must detect `IsDestroyed`, set
`_orgPanelOpen = false`, call `_orgInfoDocument.Hide()`, and refresh the country
view. `ToggleOrgInfo()` must return early while `PlayerOrganization.IsDestroyed`
so subsequent clicks on the player-org control cannot reopen it. Do not add a
destroyed USS class or leave a grayed final-state panel visible.

No change needed for non-player destroyed orgs — `OrgInfoDocument` is never
bound to any org but the player's own.

### 4. `EndGameWindowDocument` — participate in the `ModalState` convention

Current state (`Assets/Scripts/Unity/UI/EndGameWindowDocument.cs:90-102`):
`HandleStateChanged` unconditionally calls `_modalState.Lock(this)` +
`display = Flex` the instant `GameCompletion.IsCompleted` is true, with no
`IsLocked()` check and no `Unlocked` subscription — unlike
`CountryDestroyedWindowDocument`'s `TryOpenIfQueued()`/`OpenCurrent()` split.

Change:
- Split `HandleStateChanged` into a state-tracking half and an open half. Keep
  `HandleStateChanged` reacting to `GameCompletion`/`Leaderboard`/
  `PlayerOrganization`/`Locale` changes as today, but instead of directly
  locking+showing, call a new `TryOpenIfQueued()`:
  ```text
  void TryOpenIfQueued() {
      if (IsVisible) { return; }
      if (_state == null || !_state.GameCompletion.IsCompleted) { return; }
      if (_state.OrgDestroyedResults.TryPeek(out _)) { return; }
      if (_modalState.IsLocked()) { return; }
      OpenCurrent();
  }

  void OpenCurrent() {
      _modalState.Lock(this);
      _root.style.display = DisplayStyle.Flex;
      _view.Refresh(_state.GameCompletion, _state.Leaderboard, _state.PlayerOrganization, _gameSettings.EndGameComparisons);
  }
  ```
  The pending-org check is independent of lock state and must happen before
  opening; it makes subscriber invocation order irrelevant when some other
  modal releases the lock. Keep the existing "not completed → unlock + hide"
  branch in `HandleStateChanged` unchanged (that path is unaffected by the
  gating fix).
- In `Subscribe()`: add `_modalState.Unlocked += HandleModalUnlocked;`
  (`HandleModalUnlocked` calls `TryOpenIfQueued()`); unsubscribe in
  `Unsubscribe()`/`OnDisable` (mirror `CountryDestroyedWindowDocument.OnDestroy`,
  `CountryDestroyedWindowDocument.cs:72-76`, though `EndGameWindowDocument`
  currently unsubscribes state events in `OnDisable`, not `OnDestroy` — follow
  its own existing convention for where the modal-lock subscribe/unsubscribe
  lives).
- `Start()` already calls `HandleStateChanged(null, null)` once — change that
  call to also fall through to `TryOpenIfQueued()` (or have
  `HandleStateChanged`'s "completed" branch call `TryOpenIfQueued()` instead of
  locking directly), so behavior is identical to before whenever nothing else
  holds the lock (the common case — this fix only changes timing when a
  notification window is genuinely open/queued).
- `EndGameWindowView.Refresh(...)` and all other view/visual behavior is
  **unchanged** — only the open-gating logic in the Document changes.

### 5. `LastOrgStanding` presentation

Part A adds `WinConditionHintKind.LastOrgStanding` and projects the runtime
goal's `Current`/`Target` as destroyed opponents / total opponents. Consume
those values only:

- `SelectOrgDocument.FormatGoalHintRow` adds a `LastOrgStanding` case using a
  localized description such as "Be the last organization standing"; it does
  not derive participant counts.
- `GoalsWindowView.FormatDescription` adds the same kind using a localized
  runtime description such as "Destroy every rival organization". Its existing
  progress bar and `current/target` number render Part A's values; keep numeric
  formatting as integer counts.

Do not parse completion config or duplicate destroyed-opponent calculations in
Unity UI code.

### 6. Locale

Add EN keys (`Assets/Localization/en.asset`, insert beside the existing
`country_destroyed.*` block, `en.asset:571-576`):

```yaml
  - Key: org_destroyed.header_format
    Value: '{0} unravels in the shadows...'
  - Key: org_destroyed.body_format
    Value: 'Rival hands wove the threads that brought it down. {0} no longer stands among the powers of this world.'
  - Key: org_destroyed.confirm
    Value: Continue
```

Only 3 orgs exist (`Illuminati`, `Masons`, `BlackHand` — confirmed via
`organization_name.*` keys, `en.asset:361-366`), so flavor copy should read
naturally for any of the three. Use the **localization** skill for real
Russian in `ru.asset` (not an English placeholder), mirroring
`country_destroyed.*`'s `ru.asset` entries.

Also add real EN/RU entries for
`select_org.win_conditions.last_org_standing` (pre-game hint) and a goals
description key such as `goals.last_org_standing` (runtime row). Both
`SelectOrgDocument` and `GoalsWindowView` must use localization keys, not
hardcoded fallback-only English.

### 7. Scene wiring

Add a `Map.unity` GameObject `OrgDestroyedWindowUI` (`Transform` +
`OrgDestroyedWindowDocument` + `UIDocument`, same `HUDPanelSettings`
(`guid: a52ac28cceb58ba4db172389975ccca7`) as every other modal), sourceAsset
= the new UXML, beside `CountryDestroyedWindowUI`. Prefer Unity MCP
(`manage_gameobject create` + `manage_components add`) over hand-edited scene
YAML with guessed fileIDs (per `scenes.md`); treat Editor confirm as a User
Step.

## Agent Steps

- [ ] **Confirm Part A surfaces** — verify `IsOrgDestroyed`,
  `OrgDestroyedApplied`, `VisualState.OrgDestroyedResults`,
  `PlayerOrganization.IsDestroyed` exist (from Part A's plan); do not duplicate
  their creation here.

- [ ] **`OrgDestroyedWindow` UXML/USS** — clone `CountryDestroyedWindow`'s 4
  files, rename selectors/classes, reuse `country_destroy.png` reference
  verbatim (same guid/fileID).

- [ ] **Document + View** — `OrgDestroyedWindowDocument`/`OrgDestroyedWindowView`:
  FIFO open/hide, `ModalState` lock/unlock, `HideVisualOnly` on `Awake` (no
  ack), and dismissal ordered visual hide → acknowledge current snapshot while
  still locked → unlock; PointerUp + `ContainsPoint` on close and confirm,
  `sortingOrder = 515`, bind locale + org name (own `GetOrgName` copy) + image;
  no deselect logic, no pause logic.

- [ ] **Close the player org panel** — on `PlayerOrganization.IsDestroyed`,
  call `OrgInfoDocument.Hide()` to close both subpanels; in `HUDDocument` reset
  `_orgPanelOpen = false` and guard `ToggleOrgInfo()`/`Show()` against reopen.

- [ ] **`EndGameWindowDocument` sequencing fix** — split `HandleStateChanged`
  into state-tracking + `TryOpenIfQueued()`/`OpenCurrent()`; subscribe to
  `ModalState.Unlocked`; return while either `ModalState` is locked or
  `OrgDestroyedResults.TryPeek` reports pending work; rely on Part A's
  queue-before-completion publication; verify the "not completed" unlock/hide
  branch and `EndGameWindowView` itself are unchanged. Keep per-type FIFOs +
  `ModalState`; add no global queue.

- [ ] **DI registration** —
  `GameLifetimeScope.RegisterComponentInHierarchy<OrgDestroyedWindowDocument>()`
  right after `CountryDestroyedWindowDocument`.

- [ ] **Last-standing views + localization** — add cases to
  `SelectOrgDocument` and `GoalsWindowView`; add EN/RU keys for those views and
  EN/RU `org_destroyed.*` copy (real Russian, no placeholders).

- [ ] **Scene UIDocument wiring** — add GO + `UIDocument` + `HUDPanelSettings`
  in `Map.unity` (MCP or YAML); document User Step for Editor confirm.

- [ ] **Tests + validate** — see Tests; run `dotnet test
  src/GlobalStrategy.Core.sln` and Release build for plugin DLLs after any
  `src/` change (per workflow) — note most of this plan's work is
  `Assets/Scripts/Unity/UI`, not `src/`, so a Release rebuild may not be
  triggered unless Part A's `src/` changes land in the same session.

## User Steps

These steps require Unity Editor scene/asset work or visual inspection.

### 1. Confirm `OrgDestroyedWindow` scene wiring

Open `Map.unity`, select `OrgDestroyedWindowUI`, verify `UIDocument` uses the
new UXML and the same `HUDPanelSettings` as other modals. Play mode: no
missing-panel/null-root console errors.

### 2. Visual verify — open / close / confirm / modal lock

Trigger an org destroy (via Part A path or a debug command). Confirm: dark
modal appears above map/HUD with the destroyed org's name in header/body;
reused `country_destroy.png` image visible; map and other UI blocked; Close
and Confirm both dismiss, unlock, and advance FIFO if another notification is
queued.

### 3. Player org panel closure

Open `OrgInfoDocument` and each subpanel, then destroy the player's own org.
Confirm the document hides immediately, the character/action subpanel closes,
the HUD no longer considers the org panel open, and repeated player-org-control
clicks cannot reopen it. No grayed panel remains visible.

### 4. `EndGameWindow` sequencing

Force a scenario where the player's own org is destroyed and the session ends
immediately (not last-org-standing) while a `WarResultWindow` or
`CountryDestroyedWindow` already owns `ModalState`. Dismiss that first modal
and confirm the pending `OrgDestroyedWindow` opens next regardless of event
subscriber order; EndGame must not take the newly released lock. With two org
destroy snapshots queued, confirm Close/Confirm acknowledges the visible item
before unlock, the second org window opens next, and `EndGameWindow` opens only
after the final org snapshot is acknowledged. Confirm there is no flash,
reopen of an acknowledged snapshot, or stacked modal. Also confirm the common
case (no modal lock and no pending org notification) is unaffected:
`EndGameWindow` still opens immediately on a normal win.

### 5. Last-org-standing presentation

On `SelectOrg`, confirm the alternative win-condition list includes the
localized last-org-standing description in EN and RU. In the Goals window,
confirm the same condition shows integer destroyed-opponent progress over total
opponents and updates after an opponent is destroyed.

## Tests

Automated coverage is limited for UI Toolkit documents; focus on the
deterministic sequencing behaviors this plan changes:

- **No new `src/` logic in this plan** — parser/projection/progress tests and
  the converter regression asserting queue publication before completion
  belong to Part A's plan. This plan's document behavior (FIFO open/close,
  modal gating, permanent player-panel closure, and view formatting) has no
  automated UI Toolkit harness and is covered by User Steps. If document-level
  tests are introduced while implementing, cover both deterministic races:
  (1) dismissal mutates the FIFO before firing `ModalState.Unlocked`, and (2)
  EndGame refuses to open when the lock is free but
  `OrgDestroyedResults.TryPeek` is still true, including both possible
  `Unlocked` subscriber orders.
- Full suite after any `src/` edits made alongside this plan (should be none):
  `dotnet test src/GlobalStrategy.Core.sln` + Release build for plugin DLLs.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material changes; image is the existing
  `country_destroy.png` texture asset reused as-is.
- **ECS game logic** — none in this plan; destroy flag/event/queue/projection
  all Part A, under `src/`. This plan's MonoBehaviours only bind projected
  state and toggle visuals.
- **VContainer** — register `OrgDestroyedWindowDocument` in
  `GameLifetimeScope`; no ad-hoc service locators.
- **UI Toolkit only** — UXML/USS + document/view pairs; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under
  `Docs/Specs/26_08_11_09_org-destroy-ui/` after the approved spec.
- **File organisation / assemblies** — UI under `Assets/Scripts/Unity/UI` and
  `Assets/UI/Modal/OrgDestroyedWindow/`; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access
  modifiers.

Use the implement skill to start working on the plan or request changes.
