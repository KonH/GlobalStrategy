Design or update UI screens and visual assets across the project's design scopes. Each scope has its own HTML prototype file as the canonical approved design reference.

## Scopes

| Scope | Prototype file | Coverage |
|---|---|---|
| `Design/01_prototype` | `design-final.html` | UI screens (HUD, menus, panels) |
| `Design/02_icons_and_cards` | `design.html` | Action cards, character portraits, icons |

When the user specifies a scope (e.g. `@Design/02_icons_and_cards`), work in that scope's folder and prototype file. Default to `Design/01_prototype` when no scope is given.

## Image Policy

**Never use placeholder services (placehold.it, picsum, lorempixel, etc.) or third-party stock images.** For any image content needed in the prototype (character portraits, card art, icons, map thumbnails), use the project's image generation pipeline:

- For AI-generated images: invoke the `$generate-image` skill (ComfyUI + FLUX backend)
- For screenshots of existing in-game content: reference files already in the scope's `initial/` folder

If a suitable image does not yet exist, leave an empty container with a CSS background-color placeholder and a comment `<!-- TODO: generate via $generate-image -->` rather than pulling from a third-party URL.

## Workflow

1. **Read the current prototype file** for the target scope to understand the existing structure.
2. **Add or update a `<div class="screen-section">` block** for the item, following the existing section pattern.
3. **Use only the palette tokens and component classes below** — no new colours or ad-hoc styles.
4. **Open the file in a browser and verify visually** before reporting done. Describe what the mockup shows.
5. **Translate to Unity** only after the user approves the HTML design.

If the prototype file does not exist yet (e.g. a new scope), create it using the HTML shell at the bottom of this document, then add the first section.

## Palette Tokens (CSS custom properties, set on `[data-p="navy"]`)

| Token | Value | Usage |
|---|---|---|
| `--panel-bg` | `#1A2A48` | All panels, HUD boxes |
| `--panel-bdr` | `#C8A040` | Panel/button borders |
| `--btn-bg` | `#243258` | Normal button fill |
| `--btn-hov` | `#2E4070` | Button hover |
| `--btn-act` | `#C8A040` | Active/selected button (gold) |
| `--btn-2nd` | `#1C2840` | Secondary button fill |
| `--tip-bg` | `#12203A` | Tooltip background |
| `--c-dark` | `#F0E8D0` | Primary text, headers |
| `--c-mid` | `#C8B890` | Labels, gold counter, date |
| `--c-hint` | `#8888A8` | Italic hints, secondary info |
| `--c-pos` | `#50D870` | Income, buffs |
| `--c-neg` | `#E04040` | Costs, debuffs |

Speed-active button override: `background:#1E5030; color:#90FFB8; border-color:#2A7040`

## Component Classes

### Layout
- `.gs-panel` — dark navy panel with gold border, column flex, centered items. Add `.hud` for tighter HUD-specific padding.
- `.map-bg` — simulated map background (gradient). Add `.pad` for 6 px padding. Add `.centered` for center-aligned children.
- `.map-bg.pad` with `position:absolute; bottom:6px; right:6px` for HUD overlay panels.
- `.hud-bar` — full-width bottom bar, absolute-positioned inside `.map-bg`.
- `.settings-grid` — two-column CSS grid (`auto 1fr`) for label + toggle rows.
- `.states-row` — horizontal flex for showing multiple time-control states side by side.
- `.tip-sub-grid` — three-column grid for tooltip variant comparisons.

### Cards (`Design/02_icons_and_cards` scope)
- `.card` — action card frame (navy bg, gold border, fixed width ~120 px, column flex)
- `.card-header` — card title bar (darker bg, Cinzel bold, centered)
- `.card-body` — card content area (padding, description text)
- `.card-cost` — cost row at card bottom (gold text, coin icon inline)
- `.card-effect` — effect chip inside card body (`.pos` / `.neg` modifier for colour)

### Text
- `.gs-title` — large title (18 px, Cinzel bold, centered)
- `.gs-header` — section header (14 px, Cinzel bold)
- `.gs-label` — form label (11 px, IM Fell English, mid colour)
- `.gs-hint` — italic hint (10 px, IM Fell English, hint colour, centered)

### Buttons
- `.gs-btn` — base button (navy bg, gold border, bold IM Fell English)
- `.gs-btn.gs-btn--primary` — full-width tall button (28 px, Cinzel)
- `.gs-btn.gs-btn--secondary` — darker background variant
- `.gs-btn.gs-btn--small` — compact button (22 px tall, Cinzel)
- `.gs-btn.gs-btn--active` — gold fill (selected state)
- `.gs-btn.gs-btn--speed` — green fill (running speed)
- `.gs-btn.gs-btn--dest` — red fill (destructive action)

### Toggles
- `.tog-on` / `.tog-off` — exclusive-option toggle (gold/navy fill)

### Tooltip
- `.tip-box` — tooltip panel (dark navy, gold border, IM Fell English body). Add `border-style:dashed` for locked/pinned state (HTML only — dashed is not available in Unity USS).

### Icons (in `icons/` folder relative to scope, or `Design/01_prototype/icons/` as fallback)
| File | Usage | CSS tint |
|---|---|---|
| `coin.svg` | Gold counter prefix | `filter: brightness(0) invert(1) sepia(0.8) saturate(3) hue-rotate(15deg) brightness(0.82)` |
| `pause.svg` | Pause button | `filter: brightness(0) invert(0.94) sepia(0.12)` |
| `play.svg` | Play button (shown when paused) | same |
| `menu.svg` | Menu button | same |
| `control.svg` | Control counter | same |

Use `<img class="btn-icon" src="icons/X.svg">` inside buttons, `<img class="coin-icon" src="icons/coin.svg">` in counters.

## Section Template

```html
<!-- ================================================================ SCREEN NAME -->
<div class="screen-section">
  <div class="screen-header">
    <h2>Screen Name</h2>
    <!-- Add reference thumbnail if a screenshot exists in initial/ -->
    <div>
      <a href="initial/ScreenName.png" target="_blank"><img src="initial/ScreenName.png" alt="ScreenName screenshot"></a>
      <div class="ref-label">original</div>
    </div>
  </div>
  <div class="mockup-col">
    <!-- Mockup goes here using components above -->
  </div>
</div>
```

Omit the thumbnail block if there is no matching `initial/` screenshot.

## New Prototype File Shell

Use this when creating a prototype file for a new scope (e.g. `Design/02_icons_and_cards/design.html`). Copy the `<style>` block from `Design/01_prototype/design-final.html` so all tokens and component classes are available.

```html
<!DOCTYPE html>
<html lang="en" data-p="navy">
<head>
<meta charset="UTF-8">
<title>GlobalStrategy — [Scope Name]</title>
<style>
/* === paste full <style> block from design-final.html here === */
</style>
</head>
<body>
<h1>GlobalStrategy — [Scope Name]</h1>

<!-- sections go here -->

</body>
</html>
```

## HTML → Unity USS Class Mapping

| HTML class | Unity USS class | Notes |
|---|---|---|
| `.gs-panel` | `.gs-panel` | Direct 1:1 |
| `.gs-title` | `.gs-title` | |
| `.gs-header` | `.gs-header` | |
| `.gs-label` | `.gs-label` | |
| `.gs-hint` | `.gs-hint` | |
| `.gs-btn` | `.gs-btn` | |
| `.gs-btn--primary` | `.gs-btn--primary` | |
| `.gs-btn--secondary` | `.gs-btn--secondary` | |
| `.gs-btn--small` | `.gs-btn--small` | |
| `.gs-btn--active` | `.gs-btn--active` | |
| `.gs-btn--speed` | `.gs-btn--speed` | |
| `.gs-btn--dest` | `.gs-btn--destructive` | Renamed in USS |
| `.tog-on` | `.gs-toggle-on` | Renamed in USS |
| `.tog-off` | `.gs-toggle-off` | Renamed in USS |
| `.tip-box` | `.tooltip-overlay` | Structure differs — see `uitoolkit.md` |
| `--c-pos` text | `.gs-color-positive` | |
| `--c-neg` text | `.gs-color-negative` | |
| `--c-mid` text | `.gs-color-mid` | |
| `--c-hint` text | `.gs-color-hint` | |

For icons in Unity, reference them as USS `background-image` with `-unity-background-image-tint-color`. The Unity tint equivalent of the HTML button icon filter is `rgb(240, 232, 208)` (`.gs-icon` class). The coin icon tint is `rgb(200, 160, 64)` (`.gs-icon--coin`).

## Layout Conventions

- **HUD panels:** `position:absolute; top:6px; left:6px` (country) and `top:6px; right:6px` (time controls).
- **Modal panels:** centred over map background, fixed width (125–228 px depending on content).
- **Select Country panel:** `position:absolute; bottom:6px; right:6px` inside `min-height:122px` map.
- **Settings:** `.settings-grid` keeps toggles left-aligned regardless of label length.
- **Tooltips:** darker `--tip-bg` distinguishes them from panels. Locked state: `border-style:dashed`.
- **Bottom HUD bar:** `.hud-bar` spans full width, absolute at bottom of `.map-bg`.
- **Cards:** fixed width (~120 px), column flex, displayed in a horizontal row or grid.
