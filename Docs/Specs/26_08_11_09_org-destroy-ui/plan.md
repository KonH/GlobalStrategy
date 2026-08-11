# Plan: Org Destroyed Window (UI)

## Spec

Source: `Docs/Specs/26_08_11_09_org-destroy-ui/spec.md` (approved; owner
clarifications baked in — shown for every destruction including the player's
own, `OrgInfoDocument` hides/grays out for the player's own destruction, and
`EndGameWindow` must sequence after `OrgDestroyedWindow`).

When Part A marks an org destroyed, show a `CountryDestroyedWindow`-style modal
`OrgDestroyedWindow` (flavor header/body with org name, reused
`country_destroy.png` art, close + confirm) that blocks all UI via
`ModalState`, consumes an independent FIFO queue on `VisualState` (same
`Enqueue`/`TryPeek`/`AcknowledgeCurrent` pattern as `WarResultWindow`/
`CountryDestroyedWindow`). Hide/gray out `OrgInfoDocument` when the destroyed
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
  `ModalState.Lock(this)` on open; close + confirm both `Hide` → unlock +
  `AcknowledgeCurrent`.
- **Chrome** — flavor header/body naming the destroyed org (conspiracy-of-other-orgs
  framing), reused `country_destroy.png` image, close + confirm; dark theme;
  UI Toolkit only; shown for every destruction including the player's own.
- **Blocking** — full UI lock like `WarResult`/`CountryDestroyed` (not
  map-only).
- **`OrgInfoDocument`** — hides/grays out when the destroyed org is the
  player's own (`PlayerOrganization.IsDestroyed`).
- **`EndGameWindow` sequencing** — gate opening on `!ModalState.IsLocked()`;
  subscribe to `ModalState.Unlocked`; opens immediately as today when nothing
  else is queued/open.
- **Locale** — `en.asset`/`ru.asset` keys under `org_destroyed.*`; interpolate
  `organization_name.{OrgId}`.

## Goal

Ship a `CountryDestroyedWindow`-style FIFO notification modal for org
destruction, an `OrgInfoDocument` destroyed-state visual, and a fix so
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
`CountryDestroyedResults`. All three may hold `ModalState` locks; do not merge
queues or redesign stacking. `OrgDestroyedWindow` does not pause or unpause.

### 2. UXML / USS / Document / View — clone `CountryDestroyedWindow` 1:1

| Asset / type | Path |
|---|---|
| UXML / USS | `Assets/UI/Modal/OrgDestroyedWindow/OrgDestroyedWindow.uxml` + `.uss` |
| Document | `Assets/Scripts/Unity/UI/OrgDestroyedWindowDocument.cs` |
| View | `Assets/Scripts/Unity/UI/OrgDestroyedWindowView.cs` |

Clone `CountryDestroyedWindowDocument.cs`/`CountryDestroyedWindowView.cs`/
`.uxml`/`.uss` verbatim, renaming `country-destroyed-*` → `org-destroyed-*`,
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
  in `OpenCurrent`; `Hide()` = unlock + `AcknowledgeCurrent`.
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

### 3. `OrgInfoDocument` — hide/gray out on player's own destruction

`OrgInfoDocument` (`Assets/Scripts/Unity/UI/OrgInfoDocument.cs`) is bound
solely to `VisualState.PlayerOrganization`; its `Refresh()`
(`OrgInfoDocument.cs:137-169`) already gates several elements off
`org.IsValid`. Add an `org.IsDestroyed` branch there — apply a
`gs-*--destroyed` USS class (or equivalent) toggled via `EnableInClassList`
(follow the existing toggle-state pattern already used elsewhere in this file,
lines ~221-224) rather than fully removing/hiding the panel, so the player can
still see their org's final state rather than the panel vanishing outright.
`HandleOrgChanged` already fires `Refresh()` on `PlayerOrganization.PropertyChanged`
— no new subscription needed once Part A's `IsDestroyed` becomes part of that
state's `Set()`/change-notification.

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
      if (_modalState.IsLocked()) { return; }
      OpenCurrent();
  }

  void OpenCurrent() {
      _modalState.Lock(this);
      _root.style.display = DisplayStyle.Flex;
      _view.Refresh(_state.GameCompletion, _state.Leaderboard, _state.PlayerOrganization, _gameSettings.EndGameComparisons);
  }
  ```
  Keep the existing "not completed → unlock + hide" branch in
  `HandleStateChanged` unchanged (that path is unaffected by the gating fix).
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

### 5. Locale

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

### 6. Scene wiring

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
  ack), PointerUp + `ContainsPoint` on close and confirm, `sortingOrder = 515`,
  bind locale + org name (own `GetOrgName` copy) + image; no deselect logic, no
  pause logic.

- [ ] **`OrgInfoDocument` destroyed-state** — add `org.IsDestroyed` branch in
  `Refresh()`; toggle a destroyed-state USS class rather than hard-hiding the
  panel.

- [ ] **`EndGameWindowDocument` sequencing fix** — split `HandleStateChanged`
  into state-tracking + `TryOpenIfQueued()`/`OpenCurrent()`; subscribe to
  `ModalState.Unlocked`; verify the "not completed" unlock/hide branch and
  `EndGameWindowView` itself are unchanged.

- [ ] **DI registration** —
  `GameLifetimeScope.RegisterComponentInHierarchy<OrgDestroyedWindowDocument>()`
  right after `CountryDestroyedWindowDocument`.

- [ ] **Localization** — EN keys under `org_destroyed.*`; run localization
  skill for RU.

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

### 3. `OrgInfoDocument` destroyed state

Destroy the player's own org (debug path). Confirm `OrgInfoDocument` visibly
reflects the destroyed state (grayed/marked) rather than looking unchanged.

### 4. `EndGameWindow` sequencing

Force a scenario where the player's own org is destroyed and the session ends
immediately (not last-org-standing) while another notification window
(`OrgDestroyedWindow`, or a `WarResultWindow`/`CountryDestroyedWindow` that
happens to be queued the same tick) is open or queued. Confirm
`OrgDestroyedWindow` (or whichever fired first) shows and dismisses normally,
and `EndGameWindow` appears immediately after — not stacked on top of a
still-open window. Also confirm the common case (no notification pending) is
unaffected: `EndGameWindow` still opens immediately on a normal win.

## Tests

Automated coverage is limited for UI Toolkit documents; focus on the one
non-visual behavior this plan changes:

- **No new `src/` logic in this plan** — all destroy-condition/goal/completion
  tests belong to Part A's plan. If `OrgInfoDocument`'s `Refresh()` gains any
  extractable pure logic (unlikely, given it's a straightforward class-toggle),
  add a focused test; otherwise this plan's behavior (FIFO open/close, modal
  lock ordering, `EndGameWindow` gating) has no automated UI Toolkit test
  harness in this project and is covered by User Steps.
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
