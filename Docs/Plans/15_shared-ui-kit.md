# Plan 15: Shared UI Kit

## Goal

Create a single shared USS file that centralises all reusable styles (colours, panels, buttons, text variants, toggles, positive/negative text) and apply those classes throughout every existing USS/UXML file. After this, each per-feature USS only contains layout rules that are truly unique to that feature.

---

## Current State

Nine USS files each duplicate the same colour palette and structural styles:

| Pattern | Appears in |
|---|---|
| `.menu-button` (button base + hover) | 5 files |
| Panel background `rgb(238, 222, 180)` + border | all 9 files |
| `.resource-positive` / `.resource-negative` | 3 files |
| `.window-root` / `.window-panel` / `.window-title` | 2 files |
| `.menu-panel` / `.menu-title` / `.blackfade` | 2 files |

---

## Shared Style Catalogue

The new file `Assets/UI/Shared/SharedStyles.uss` will define:

### Colour utility classes (single-property atomic styles)

Each class sets exactly one USS property. Use them standalone for one-off overrides, or rely on them being bundled inside the compound classes below.

**Background colours**
- `.gs-bg-panel` — `background-color: rgb(238, 222, 180)`
- `.gs-bg-button` — `background-color: rgb(210, 190, 140)`
- `.gs-bg-button-hover` — `background-color: rgb(180, 155, 100)`
- `.gs-bg-button-active` — `background-color: rgb(160, 120, 50)`
- `.gs-bg-tooltip` — `background-color: rgb(245, 232, 195)`

**Border colours**
- `.gs-border-primary` — `border-color: rgb(120, 85, 30)`
- `.gs-border-muted` — `border-color: rgb(160, 130, 70)`

**Text colours**
- `.gs-color-dark` — `color: rgb(55, 35, 10)` (primary text)
- `.gs-color-mid` — `color: rgb(75, 50, 15)` (secondary / label text)
- `.gs-color-hint` — `color: rgb(110, 80, 35)` (muted / hint text)
- `.gs-color-positive` — `color: rgb(30, 110, 40)` (green — income, positive deltas)
- `.gs-color-negative` — `color: rgb(160, 35, 25)` (red — costs, negative deltas)
- `.gs-color-light` — `color: rgb(245, 232, 195)` (light text on dark/active backgrounds)

The compound classes (`.gs-btn`, `.gs-panel`, etc.) bake in the appropriate colour utility values directly, so you only need to add individual colour classes for overrides or novel elements.

### Panel
- `.gs-panel` — beige background, brown border (2 px), 6 px radius, column flex, centered items

### Overlay / Modal backdrop
- `.gs-modal-root` — absolute full-screen, center-center flex
- `.gs-blackfade` — absolute full-screen, `rgba(0,0,0,0.4)` dark overlay

### Text
- `.gs-title` — 42 px, dark brown, bold (large modal title)
- `.gs-header` — 36 px, dark brown, bold (window/section header)
- `.gs-label` — 20 px, medium brown, normal weight (form label / secondary info)
- `.gs-content` — 18 px, medium brown (body / description text)
- `.gs-hint` — 16 px, lighter brown, italic (hints, descriptions)
- `.gs-color-positive` — green (income, positive deltas) — from colour utilities
- `.gs-color-negative` — red (costs, negative deltas) — from colour utilities

### Buttons
- `.gs-btn` — base button: tan background, brown border, dark text, bold, 4 px radius
- `.gs-btn:hover` — darker tan hover state
- `.gs-btn--primary` — large main-action button (full menu button size, 30 px font)
- `.gs-btn--secondary` — slightly lighter tone (non-destructive secondary actions)
- `.gs-btn--small` — compact row button (row actions, time controls)
- `.gs-btn--destructive` — red-tinted background (delete actions)
- `.gs-btn--active` — darker/selected state (active speed, active toggle-on)

### Toggle buttons (two exclusive states)
- `.gs-toggle-on` — alias for `.gs-btn--active` (darker background, light text)
- `.gs-toggle-off` — alias for `.gs-btn` base (standard tan, dark text)

---

## Approach

1. Create `Assets/UI/Shared/SharedStyles.uss` with all shared classes above.
2. Create matching `Assets/UI/Shared/SharedStyles.uss.meta` (GUID).
3. Reference the shared USS from every UXML root via `<Style src="...SharedStyles.uss"/>`.
4. In each per-feature USS, **replace** duplicated declarations with the shared class names:
   - Remove any `.menu-button`, `.resource-positive`, `.resource-negative`, `.window-root`, `.window-panel`, `.window-title`, `.menu-panel`, `.menu-title`, `.blackfade` that now live in shared.
   - Retain only positioning and sizing overrides that are truly local (e.g. `width: 300px` on a specific button instance can stay as a modifier or inline).
5. In UXML, add the shared class alongside any existing local class on each element:
   - e.g. `<ui:Button class="gs-btn gs-btn--primary menu-button" …/>`
   - Or remove the local class entirely if the shared class is sufficient.
6. Verify in Play mode that all screens look identical to before the refactor.
7. Update the rule document (`.claude/rules/unity/uitoolkit.md`) with the new UI Kit section.

---

## Steps

### 1 — Create shared USS
- Write `Assets/UI/Shared/SharedStyles.uss` with all canonical styles listed above.
- Write `Assets/UI/Shared/SharedStyles.uss.meta`.

### 2 — Add `<Style>` reference to every UXML
Files to update: `HUD.uxml`, `Time.uxml`, `PlayerCountry.uxml`, `CountryInfo.uxml`, `MainMenu.uxml`, `SelectCountry.uxml`, `LoadWindow.uxml`, `SettingsWindow.uxml`, `GameMenu.uxml`.

### 3 — Refactor HUD USS files
- `HUD.uss`: replace `.hud-menu-button`, `.hud-hamburger-button`, `.hud-debug-toggle`, `.debug-panel-button` → `gs-btn` base + small/hover; keep positional rules.
- `Time.uss`: `#time-controls Button` → `gs-btn gs-btn--small`; `.active` → `gs-btn--active`; keep size/margin.
- `PlayerCountry.uss`: `.player-country-name` → `gs-header`; `.resource-label` → `gs-label`; drop `.resource-positive/.resource-negative` (use `gs-color-positive/gs-color-negative`).
- `CountryInfo.uss`: `.country-name` → `gs-title`; same resource label/colour consolidation.

### 4 — Refactor Modal USS files
- `MainMenu.uss`: `.menu-panel` → `gs-panel`; `.blackfade` → `gs-blackfade`; `.menu-title` → `gs-title`; `.menu-button` → `gs-btn gs-btn--primary`.
- `GameMenu.uss`: same as MainMenu.
- `SelectCountry.uss`: `.info-panel` → `gs-panel`; `.country-name` → `gs-header`; `.hint-label` → `gs-hint`; `.menu-button` → `gs-btn gs-btn--primary`; `.menu-button--secondary` → `gs-btn gs-btn--secondary`.
- `LoadWindow.uss`: `.window-root` → `gs-modal-root`; `.window-panel` → `gs-panel`; `.window-title` → `gs-header`; `.menu-button` → `gs-btn gs-btn--primary`; `.row-button` → `gs-btn gs-btn--small`; `.row-button-delete` → `gs-btn gs-btn--destructive`; `.save-country` → `gs-label`; `.save-date` → `gs-content`.
- `SettingsWindow.uss`: `.window-root` → `gs-modal-root`; `.window-panel` → `gs-panel`; `.window-title` → `gs-header`; `.setting-label` → `gs-label`; `.setting-button` → `gs-btn gs-btn--small`; `.setting-button--active` → `gs-toggle-on`; `.menu-button` → `gs-btn gs-btn--primary`.

### 5 — Update UXML class attributes
Add shared classes to elements in each UXML matching the refactored USS (so the shared rules apply at the element level).

### 6 — Refresh Unity and verify
- Call `refresh_unity`.
- Check `read_console` for errors.
- Visual-inspect all screens in Play mode.

### 7 — Update uitoolkit rule
Add a **Shared UI Kit** section to `.claude/rules/unity/uitoolkit.md` explaining:
- Location: `Assets/UI/Shared/SharedStyles.uss`
- How to reference it from UXML
- Full class catalogue (panel, text, button variants, toggles, colours)
- Rule: new components must use shared classes; only add local USS for layout/sizing overrides
- How to introduce a new shared style (add to SharedStyles.uss, document in the rule, use in UXML)

---

## File Impact Summary

| File | Change |
|---|---|
| `Assets/UI/Shared/SharedStyles.uss` (new) | All shared styles |
| `Assets/UI/Shared/SharedStyles.uss.meta` (new) | Unity asset meta |
| `Assets/UI/HUD/HUD.uss` | Remove duplicates, keep layout |
| `Assets/UI/HUD/Time/Time.uss` | Replace button styles |
| `Assets/UI/HUD/PlayerCountry/PlayerCountry.uss` | Replace text/colour styles |
| `Assets/UI/HUD/CountryInfo/CountryInfo.uss` | Replace text/colour styles |
| `Assets/UI/Modal/MainMenu/MainMenu.uss` | Replace panel/button/title styles |
| `Assets/UI/Modal/GameMenu/GameMenu.uss` | Replace panel/button/title styles |
| `Assets/UI/Modal/SelectCountry/SelectCountry.uss` | Replace panel/button/text styles |
| `Assets/UI/Modal/LoadWindow/LoadWindow.uss` | Replace panel/button/text styles |
| `Assets/UI/Modal/SettingsWindow/SettingsWindow.uss` | Replace panel/button/toggle styles |
| All 9 `.uxml` files | Add `<Style>` import + class attributes |
| `.claude/rules/unity/uitoolkit.md` | Add Shared UI Kit section |

---

Use /implement to start working on the plan or request changes.
