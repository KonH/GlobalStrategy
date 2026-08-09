# Spec: Country Destroyed Window (UI)

## Feature Intent

As a player, I want to be clearly notified with an in-world, flavorful window when a country loses all of its provinces and is destroyed, so that I understand this significant world event happened and can no longer interact with that country as if it still existed.

## Acceptance Criteria

- **Given** a country has just been destroyed (per the upstream domain destroy event / `IsDestroyed` flag produced by the Part-A domain logic) **When** the destroy event is observed by the UI layer **Then** a `CountryDestroyedWindow` is shown, following the existing notification/confirmation window pattern used by `WarResultWindowDocument`/`WarResultWindowView` (FIFO queue on a `VisualState` sub-state with `Enqueue`/`TryPeek`/`AcknowledgeCurrent`, `TryOpenIfQueued()`/`OpenCurrent()`, `ModalState.Lock(this)` on open).
- **Given** the `CountryDestroyedWindow` is open **Then** it displays: a fictional/flavor header that includes the destroyed country's name; a placeholder image element (intended to later host the "revenge card" action image or an equivalent asset); fictional/flavor common/body text that also includes the destroyed country's name; a close button; and a confirmation button below the body text/image.
- **Given** the `CountryDestroyedWindow` is open **When** the player presses the close button **Then** the window hides itself, calls `ModalState.Unlock(this)`, and calls `AcknowledgeCurrent()` to pop the queue so the next queued notification (if any) can display — mirroring the existing `WarResultWindowDocument` close/confirm handling on `PointerUpEvent`.
- **Given** the `CountryDestroyedWindow` is open **When** the player presses the confirmation button **Then** it behaves the same as the close button (hide + unlock + acknowledge), since the confirmation button is the sole action available (no alternate branching outcome is implied by the issue).
- **Given** the `CountryDestroyedWindow` is open **Then** map interaction underneath is blocked for the duration, consistent with other modal windows that check `ModalState.IsLocked()` (e.g. camera pan/zoom, map clicks already respect this lock).
- **Given** the country-info side view (`CountryInfoView`, bound to `VisualState.SelectedCountryState`) is currently open for the country that just got destroyed **When** the destroy event is processed **Then** that country is deselected (e.g. by pushing `SelectCountryCommand("")`), which causes `CountryInfoView` to hide (`selected.IsValid` becomes false, `_root.style.display = None`) — regardless of whether the `CountryDestroyedWindow` notification is shown immediately or queued behind another notification.
- **Given** a country has been destroyed **When** the player subsequently attempts to select that country again through any existing selection path (e.g. clicking it on the map) **Then** the selection does not succeed and `CountryInfoView` does not reopen for that country — enforced by a guard in `SelectCountrySystem.Update` that no-ops (skips adding `IsSelected`) when the target country entity carries the destroyed flag from the Part-A domain logic.
- **Given** the `CountryDestroyedWindow` has already been shown and acknowledged (closed) for a given destroyed country **When** any other game system or UI flow re-evaluates that country's state afterward **Then** the window is not shown again for that same destruction event (the queue only enqueues once per destroy event, matching the one-shot notification pattern of `WarResultWindow`).
- **Given** all header, body/flavor text, and button labels shown in `CountryDestroyedWindow` **Then** they are sourced from locale keys added to the existing localization asset pair (`Assets/Localization/en.asset` / `ru.asset`) with the destroyed country's name interpolated into the header and body strings, not hardcoded — following the project's established localization conventions.
- **Given** the `CountryDestroyedWindow` UI implementation **Then** it follows the existing modal window structure used elsewhere in the project: UXML/USS under `Assets/UI/Modal/CountryDestroyedWindow/`, a `CountryDestroyedWindowDocument.cs` MonoBehaviour under `Assets/Scripts/Unity/UI/` using VContainer DI for injected services such as `ModalState` and (if needed for non-modal hit-testing elsewhere) `UIPointerState`.

## Out of Scope

- The underlying domain/ECS logic that determines a country has lost all provinces and marks it destroyed (persistent `IsDestroyed` flag component, the country-destroy event component exposed to `VisualState`, control pool zeroing, relation removal, card-unplayable reason, goals update) — this is covered by the separate Part-A spec and is treated here only as an already-existing upstream trigger/precondition.
- Any change to map rendering/visibility/interactivity of a destroyed country's territory beyond what is strictly needed to prevent reselection via `SelectCountrySystem` — the country stays in `VisualState.WorldCountries.CountryIds` (per Part A) and naturally has no territory to render since it owns no provinces; no grey-out/border-removal work here.
- Any gameplay/economic consequences of destruction (diplomacy, AI behavior, war state cleanup, goals content, card playability) beyond closing/blocking the selected-country UI — those are Part A concerns.
- Producing the final flavor/header copy text (beyond the direction below) or sourcing/generating the final placeholder image asset — the image asset already exists at `Assets/Textures/Events/country_destroy.png` (added separately) and is reused as-is; exact final English/Russian copy is authored during implementation following the direction below.
- Any change to how other notification windows (`WarResultWindow`, `EndGameWindow`, `GoalsWindow`) are implemented; `CountryDestroyedWindow` is a new, independent window that merely follows their established pattern.

## Resolved Decisions

(Owner clarifications from the issue thread, superseding the original ambiguities.)

- **Placeholder image asset:** `Assets/Textures/Events/country_destroy.png` (already added to the repo) — use it directly, not the revenge-card action image.
- **FIFO queuing:** confirmed — multiple destroyed-country notifications queue and display one after another, matching `WarResultWindow`'s pattern.
- **Flavor copy direction:** dark theme; header along the lines of "`{CountryName}` is lost in the past..." (exact final English/Russian wording authored during implementation, following this direction).
- **Modal blocking:** `CountryDestroyedWindow` blocks all UI like other existing fullscreen/modal windows (not just the map) — same `ModalState` convention as `WarResultWindow`/`EndGameWindow`.
- **`CountryInfoView` close timing:** close immediately when the destroy event is processed, regardless of whether the `CountryDestroyedWindow` notification itself is shown immediately or queued behind another notification.
- **Map appearance:** no explicit map appearance change in this spec or Part A — a destroyed country simply has no provinces left to render, so it is naturally invisible on the map without special-casing.
