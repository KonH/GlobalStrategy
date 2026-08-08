# Plan: Country Destroyed Window (UI)

## Spec

Source: `Docs/Specs/26_08_07_08_country-destroy-ui/spec.md` (approved; owner
clarifications baked in).

When Part A marks a country destroyed, show a dark-themed modal
`CountryDestroyedWindow` (flavor header/body with country name, event image,
close + confirm) that blocks all UI via `ModalState`, consumes an independent
FIFO queue on `VisualState` (same `Enqueue` / `TryPeek` / `AcknowledgeCurrent`
pattern as `WarResultWindow`), immediately deselects the destroyed country so
`CountryInfoView` closes even if the modal is queued behind another
notification, and guards `SelectCountrySystem` so destroyed countries cannot be
reselected. No auto-pause. No map appearance changes.

**Depends on Part A (domain):** `Docs/Specs/26_08_07_08_country-destroy-logic/`
(spec approved; implement/plan that work first or in parallel with assumed
surfaces). Part A owns:

| Surface | Assumed name (align with Part A implement) |
|---|---|
| Persistent flag | `IsDestroyed` on the country entity |
| One-shot ECS event | `CountryDestroyedApplied` (`CountryId`; created this tick, swept **next** tick like `WarResolvedApplied`) |
| VisualState FIFO | `VisualState.CountryDestroyedResults` (`CountryDestroyedResultsState`, mirror of `WarResultsState`) with snapshot entries (at least `CountryId`) |
| Converter | `VisualStateConverter` enqueues once per `CountryDestroyedApplied` |

This UI plan **does not** recreate the VisualState queue or converter enqueue —
it consumes those Part A surfaces only.

Acceptance criteria (condensed):
- **Modal** — open from FIFO via `TryOpenIfQueued` / `OpenCurrent`;
  `ModalState.Lock(this)` on open; close + confirm both `Hide` → unlock +
  `AcknowledgeCurrent` (PointerUp + `ContainsPoint`).
- **Chrome** — flavor header (`"{CountryName} is lost in the past..."` direction),
  body with country name, image `Assets/Textures/Events/country_destroy.png`,
  close + confirm; dark theme; UI Toolkit only.
- **Blocking** — full UI lock like WarResult/EndGame (not map-only).
- **Immediate deselect** — on destroy queue observe / `PropertyChanged`, push
  `SelectCountryCommand("")` when the selected country matches a destroyed id —
  do **not** wait for `OpenCurrent`.
- **Reselect guard** — `SelectCountrySystem` skips adding `IsSelected` when the
  target has `IsDestroyed`.
- **One-shot** — no re-show after acknowledge (queue enqueue once per event; Part A).
- **Locale** — `en.asset` / `ru.asset` keys; interpolate `country_name.{id}`.

## Goal

Ship a WarResult-style FIFO notification modal for country destruction with
simpler Goals/EndGame-like chrome, plus selection cleanup/guard so destroyed
countries cannot keep or regain `CountryInfoView` — without touching Part A
destroy math, VisualState queue creation, map rendering, or Events config SOs.

## Approach

### 1. Consume Part A VisualState FIFO (no duplicate queue)

Assume Part A exposes:

```text
VisualState.CountryDestroyedResults : CountryDestroyedResultsState
  Enqueue(CountryDestroyedSnapshotState)
  TryPeek(out ...)
  AcknowledgeCurrent()
  Entries / PropertyChanged  // same contract as WarResultsState
```

`CountryDestroyedSnapshotState` needs at least `CountryId` for binding and
deselect matching. UI document self-drives from this queue (WarResult/EndGame
style) — no HUD click forwarder, no pause/`ShouldPause`, no Events notification
config.

**Modal coexistence:** `CountryDestroyedResults` is independent of
`VisualState.WarResults`. Both may hold `ModalState` locks; do not merge queues
or redesign stacking. Destroy window does not pause or unpause.

### 2. UXML / USS / Document / View

| Asset / type | Path |
|---|---|
| UXML / USS | `Assets/UI/Modal/CountryDestroyedWindow/CountryDestroyedWindow.uxml` + `.uss` |
| Document | `Assets/Scripts/Unity/UI/CountryDestroyedWindowDocument.cs` |
| View | `Assets/Scripts/Unity/UI/CountryDestroyedWindowView.cs` |

Layout (simpler than WarResult; mirror Goals blackfade + panel):

- Root + `gs-blackfade` + panel
- Header label (flavor + country name)
- Image element for `country_destroy.png`
- Body/flavor label
- `btn-close` (X) and confirm button below body/image

**Document behaviour** (copy WarResult lifecycle, drop pause):

- `sortingOrder ≈ 515` (same band as WarResult `510`, below EndGame)
- `HideVisualOnly()` on Awake (display none only — **do not** Unlock/Acknowledge);
  `ModalState.Lock(this)` in `OpenCurrent`; user dismiss `Hide` = Unlock +
  `AcknowledgeCurrent` (mirror `WarResultWindowDocument`)
- Subscribe to `CountryDestroyedResults` + locale; when not visible and queue
  non-empty → `TryOpenIfQueued` → `OpenCurrent` (peek, show, bind)
- Close and confirm: `PointerUpEvent` + `ContainsPoint` (Unity 6000.4.1 bug);
  both call the same user-dismiss `Hide` (unlock + `AcknowledgeCurrent` →
  PropertyChanged opens next FIFO item)
- Inject: `VisualState`, `ILocalization`, `ModalState`,
  `IWriteOnlyCommandAccessor` (for immediate deselect). `UIPointerState` only if
  needed for non-modal hit-testing elsewhere — not required for this modal path
- Register `CountryDestroyedWindowDocument` in `GameLifetimeScope` via
  `RegisterComponentInHierarchy` next to `WarResultWindowDocument`

**View:** bind header/body via `string.Format` on locale formats +
`country_name.{CountryId}`; set confirm button label from locale; assign image
(see §4).

### 3. Immediate deselect + SelectCountrySystem guard

**Immediate deselect (UI host, this plan):** on
`CountryDestroyedResults.PropertyChanged`, on the Part A persistent destroyed
projection’s `PropertyChanged` (e.g. `WorldCountries.DestroyedCountryIds` /
`DestroyedCountriesState` — use whichever Part A lands), and on initial
subscribe: if `SelectedCountryState.IsValid` and the selected `CountryId` is in
**either** any queued destroy snapshot **or** the persistent destroyed set,
push `SelectCountryCommand("")` via `IWriteOnlyCommandAccessor`. Do this in the
Document (or a tiny shared helper) **before / regardless of** `OpenCurrent` —
so `CountryInfoView` hides when the modal is queued, and also after load when
the FIFO is empty but `IsDestroyed` is already set.
(Prefer Part A also `Remove<IsSelected>` inside `TryDestroyIfNoProvinces` so
converter drops selection the same tick as destroy; UI command remains the
spec’d safety net.)

**Reselect guard (domain — prefer Part A):** in
`src/Game.Systems/SelectCountrySystem.Update`, when resolving the target country
entity for a non-empty `CountryId`, if `world.Has<IsDestroyed>(e)` then do **not**
add `IsSelected` for that entity (selection fails). Empty `CountryId` deselection
path stays unchanged. **Skip this step if Part A's plan already lands the same
guard** (logic plan now owns it).

**Existing selection on destroy (coordinate with Part A):** the guard alone does
**not** clear an entity that already has `IsSelected` when `IsDestroyed` is
added. Part A’s `TryDestroyIfNoProvinces` should `Remove<IsSelected>` on the
destroyed country (and the load/init zero-province pass should strip
`IsSelected` from any country that is or becomes destroyed) so
`VisualStateConverter` drops `SelectedCountryState` in the same tick as the
FIFO enqueue. UI `SelectCountryCommand("")` remains as the AC’d backup,
especially when the destroy window is only queued.

No map grey-out / border work.

### 4. Image asset

- Source: `Assets/Textures/Events/country_destroy.png` (exists; currently a Git
  LFS pointer; **no `.meta` in tree yet**).
- Bind via USS `background-image: url("...")` **or** `[SerializeField] Sprite`
  after Unity import — either is fine; prefer the pattern already used for
  similar modal art if one exists, else SerializeField after import is safer
  with LFS.
- Do **not** invent an Events config ScriptableObject or reuse revenge-card art.

### 5. Localization

Add EN keys (names flexible, suggested):

- `country_destroyed.header_format` — e.g. `"{0} is lost in the past..."`
- `country_destroyed.body_format` — flavor body including `{0}` country name
- `country_destroyed.confirm` — confirm button label

Interpolate with localized `country_name.{id}`. At implement time use the
**localization** skill for real Russian in `ru.asset` (no English placeholders
in RU).

### 6. Scene wiring

Add a `Map.unity` GameObject (e.g. `CountryDestroyedWindowUI`) with `UIDocument`
+ same `HUDPanelSettings` as other modals, UXML assigned, beside
`WarResultWindowUI`. Agent may attempt Unity MCP / YAML; treat Editor verify as
User Step.

## Agent Steps

- [x] **Confirm Part A surfaces** — verify (or land behind) `IsDestroyed`,
  `CountryDestroyedApplied`, and `VisualState.CountryDestroyedResults` FIFO +
  converter enqueue; do not duplicate queue creation in this plan. If Part A is
  not yet merged, stub only against the agreed names above.

- [x] **SelectCountrySystem guard** — skip if already implemented in the logic
  plan; otherwise in `src/Game.Systems/SelectCountrySystem.cs`, skip adding
  `IsSelected` when the target country has `IsDestroyed`; keep empty-id
  deselect behaviour.

- [x] **CountryDestroyedWindow UXML/USS** — create
  `Assets/UI/Modal/CountryDestroyedWindow/` with dark-themed panel: header, image
  slot, body, `btn-close`, confirm button; import `SharedStyles`; layout-only
  feature USS.

- [x] **Document + View** — `CountryDestroyedWindowDocument` /
  `CountryDestroyedWindowView`: FIFO open/hide, `ModalState` lock/unlock,
  `HideVisualOnly` on Awake (no ack), PointerUp + ContainsPoint on close and
  confirm (both → same user-dismiss Hide), `sortingOrder ≈ 515`, bind locale +
  country name + image; no pause logic.

- [x] **Immediate deselect wiring** — on `CountryDestroyedResults` + Part A
  destroyed-set `PropertyChanged` (and initial subscribe), push
  `SelectCountryCommand("")` when selected id is queued **or** in the persistent
  destroyed set; independent of whether the window opens now or later.

- [x] **DI registration** — `GameLifetimeScope.RegisterComponentInHierarchy<CountryDestroyedWindowDocument>()`.

- [x] **Localization** — EN keys under `country_destroyed.*`; run localization
  skill for RU.

- [x] **Scene UIDocument wiring** — add GO + `UIDocument` + `HUDPanelSettings` in
  `Map.unity` (MCP or YAML); document User Step for Editor confirm.

- [x] **Tests + validate** — see Tests; run `dotnet test src/GlobalStrategy.Core.sln`
  and Release build for plugin DLLs after any `src/` change (per workflow).

## User Steps

These steps require Unity Editor scene/asset work, visual inspection, or LFS.

### 1. LFS pull + import `country_destroy.png`

The file is a Git LFS pointer and has no `.meta` yet. Run `git lfs pull` (or
equivalent) so the real PNG is present, open the project in Unity so it imports
and generates `.meta`, then confirm the Document/View/USS reference resolves
(no pink/missing sprite).

### 2. Confirm CountryDestroyedWindow scene wiring

Open `Map.unity`, select `CountryDestroyedWindowUI` (or equivalent), verify
`UIDocument` uses the CountryDestroyedWindow UXML and the same `HUDPanelSettings`
as other modals. Play mode: no missing-panel / null-root console errors.

### 3. Visual verify — open / close / confirm / modal lock

Force a country destroy (or enqueue a test destroy via Part A path). Confirm:
dark modal appears above map/HUD; header/body show localized country name; image
visible; map and other UI blocked; Close and Confirm both dismiss, unlock, and
advance FIFO if another destroy is queued. No auto-pause from this window.

### 4. CountryInfoView + reselect

With `CountryInfoView` open for a country, destroy that country (including when
another modal is already up so the destroy window is queued). Confirm the info
view closes immediately. After acknowledge, clicking that country (any selection
path) must not reopen `CountryInfoView`.

## Tests

Automated coverage is limited for UI Toolkit documents; focus on domain/selection
and any pure helpers:

- **`SelectCountrySystem` tests (new or extend existing)** — selecting a country
  with `IsDestroyed` does not add `IsSelected`; selecting a normal country still
  works; `SelectCountryCommand("")` still clears selection even if a destroyed
  country exists.
- **FIFO consumption (if tested from Main, optional)** — if Part A already tests
  enqueue, add/extend a thin test that `AcknowledgeCurrent` drains
  `CountryDestroyedResults` in order (UI host contract). Do not re-test destroy
  math here.
- **No Unity UI test harness** — modal layout, image, PointerUp hit-testing,
  ModalState feel, and immediate deselect-while-queued covered by User Steps.
- Full suite after `src/` edits: `dotnet test src/GlobalStrategy.Core.sln` +
  Release build for plugin DLLs.

## Constitution Check

Checked against `Docs/Constitution.md`.

No conflicts found — plan aligns with all principles.

- **Rendering** — no RP/shader/material changes; image is an existing texture
  asset only.
- **ECS game logic** — destroy flag/event/queue stay Part A under `src/`; this
  plan’s only `src/` touch is the `SelectCountrySystem` selection guard (domain
  rule required by UI AC). MonoBehaviours bind projected state and emit
  select-clear / modal close only.
- **VContainer** — register `CountryDestroyedWindowDocument` in
  `GameLifetimeScope`; no ad-hoc service locators.
- **UI Toolkit only** — UXML/USS + document/view pair; no Canvas/uGUI.
- **Plan / spec discipline** — colocated under
  `Docs/Specs/26_08_07_08_country-destroy-ui/` after the approved spec.
- **File organisation / assemblies** — UI under `Assets/Scripts/Unity/UI` and
  `Assets/UI/Modal/CountryDestroyedWindow/`; no new asmdef.
- **C# style** — tabs, braces, `_` private fields, no redundant access modifiers.

Use the implement skill to start working on the plan or request changes.
