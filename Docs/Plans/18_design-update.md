# 18 — Design Update: Navy Palette, Fonts, Icons

## Goal

Translate the approved UI design from `Design/01_prototype/design-final.html` into Unity.  
Four concrete deliverables: palette overhaul, custom fonts, SVG icons, and UXML/layout fixes.

---

## Approach

Work bottom-up: shared styles first, then font assets, then icon assets, then per-screen UXML fixes.  
No game-logic changes — this is purely visual.

---

## Steps

### 1. Palette — SharedStyles.uss

Replace all colour values in `Assets/UI/Shared/SharedStyles.uss` with the navy palette from the design:

| Token | New value | Used for |
|---|---|---|
| Panel bg | `rgb(26, 42, 72)` `#1A2A48` | `.gs-panel`, `.gs-bg-panel` |
| Border | `rgb(200, 160, 64)` `#C8A040` | All panel and button borders |
| Button bg | `rgb(36, 50, 88)` `#243258` | `.gs-btn`, `.gs-bg-button` |
| Button hover | `rgb(46, 64, 112)` `#2E4070` | `.gs-btn:hover` |
| Button active | `rgb(200, 160, 64)` `#C8A040` | `.gs-btn--active`, `.gs-bg-button-active`, `.gs-toggle-on` |
| Button secondary bg | `rgb(28, 40, 64)` `#1C2840` | `.gs-btn--secondary` |
| Button destructive bg | `rgb(106, 24, 24)` `#6A1818` | `.gs-btn--destructive` |
| Button destructive border | `rgb(154, 32, 32)` `#9A2020` | `.gs-btn--destructive` |
| Button destructive text | `rgb(248, 232, 200)` `#F8E8C8` | `.gs-btn--destructive` |
| Text dark | `rgb(240, 232, 208)` `#F0E8D0` | `.gs-color-dark`, titles, headers |
| Text mid | `rgb(200, 184, 144)` `#C8B890` | `.gs-color-mid`, labels |
| Text hint | `rgb(136, 136, 168)` `#8888A8` | `.gs-color-hint`, hints |
| Positive | `rgb(80, 216, 112)` `#50D870` | `.gs-color-positive` |
| Negative | `rgb(224, 64, 64)` `#E04040` | `.gs-color-negative` |
| Light | `rgb(240, 232, 208)` `#F0E8D0` | `.gs-color-light` |
| Tooltip bg | `rgb(18, 32, 58)` `#12203A` | `.gs-bg-tooltip` |

Also add a new speed-active class to SharedStyles.uss:
```css
.gs-btn--speed {
    background-color: rgb(30, 80, 48);   /* #1E5030 */
    color: rgb(144, 255, 184);           /* #90FFB8 */
    border-color: rgb(42, 112, 64);      /* #2A7040 */
}
```


Text sizes stay the same — only colours change.

Also update all per-feature USS files that contain hardcoded old-palette values.  
Known files: `Assets/UI/HUD/HUD.uss` (`.player-country-panel`, `.country-info-panel`, `.tooltip-overlay`, `.tooltip-header`, `.tooltip-effect-positive`, `.tooltip-effect-negative`, `.tooltip-description`, `.debug-panel-inner`).  
Scan remaining USS files under `Assets/UI/` for any other beige/brown hex/rgb literals and replace them with the new navy values.

---

### 2. Font Assets

**2a. Copy TTF files**

Copy the four TTFs from `Design/01_prototype/fonts/` to `Assets/UI/Fonts/`:
- `Cinzel-Regular.ttf`
- `Cinzel-Bold.ttf`
- `IMFellEnglish-Regular.ttf`
- `IMFellEnglish-Italic.ttf`
- `PlayfairDisplay-Regular.ttf`
- `PlayfairDisplay-Bold.ttf`

**2b. Create Font Assets in Unity Editor**

For each TTF, generate a UI Toolkit-compatible FontAsset via  
`Window → TextMeshPro → Font Asset Creator` (or the automatic importer in Unity 6).  
Save as `Assets/UI/Fonts/<Name>.asset`.

Create font assets for:
- `Cinzel-Regular.asset`
- `Cinzel-Bold.asset`
- `IMFellEnglish-Regular.asset`
- `IMFellEnglish-Italic.asset`
- `PlayfairDisplay-Regular.asset` (used as Cyrillic fallback)
- `PlayfairDisplay-Bold.asset`

Set Playfair Display as a fallback in both Cinzel and IM Fell English font assets  
(Edit the FontAsset → Fallback Font Assets list).

**2c. Wire fonts into SharedStyles.uss**

Add `-unity-font-definition` references to the relevant classes:
- `.gs-title`, `.gs-header`, `.gs-btn--primary`, `.gs-btn--small` → Cinzel Bold
- `.gs-label`, `.gs-content`, `.gs-hint`, `.gs-btn` → IM Fell English Regular
- `.gs-hint` → IM Fell English Italic

Example USS syntax:
```css
.gs-title {
    -unity-font-definition: url("project://database/Assets/UI/Fonts/Cinzel-Bold.asset");
}
```

---

### 3. Icon Assets

**3a. Convert and import icons**

Unity cannot import SVGs as `Texture2D` without the `com.unity.vectorgraphics` package (not in this project).  
Convert each SVG from `Design/01_prototype/icons/` to a 128×128 PNG (2× for crisp display), then copy to `Assets/UI/Icons/`:
- `coin.png`
- `pause.png`
- `play.png`
- `menu.png`

Import each as `Texture2D`, texture type `Sprite (2D and UI)`, `Read/Write` off, max size 128.

**3b. Add USS icon classes**

Add to `SharedStyles.uss`:
```css
.gs-icon {
    width: 14px;
    height: 14px;
    -unity-background-image-tint-color: rgb(240, 232, 208);  /* c-dark */
}

.gs-icon--coin {
    width: 13px;
    height: 13px;
    -unity-background-image-tint-color: rgb(200, 160, 64);   /* gold */
}
```

Individual icon `background-image` values are set in local USS files (not shared), pointing to the specific asset.

---

### 4. UXML / USS fixes per screen

#### 4a. HUD — Time panel (`Assets/UI/HUD/Time/Time.uxml`)

- Replace `btn-pause` text `"||"` with an empty string; add class `gs-icon-btn--pause` to the button (local USS provides `background-image` pointing to `pause.svg`)
- The button's C# code already swaps the active state; it will also need to swap the background-image between `pause.svg` and `play.svg` — do this via `AddToClassList`/`RemoveFromClassList` on `gs-icon-btn--pause` / `gs-icon-btn--play`

#### 4b. HUD — HUD.uxml

- Replace `btn-menu` text `"|||"` with empty string; add class `gs-icon-btn--menu`  
  (local `HUD.uss` provides `background-image` pointing to `menu.svg`)
- Move `btn-menu` inside the `top-right-panel` below the `Time` instance (it's already there; just update text and class)

#### 4c. HUD — PlayerCountry panel

The gold counter (`resources-container`) is built dynamically in C#.  
The `ResourcesView` already creates `VisualElement` for the coin icon placeholder — replace the placeholder with a `VisualElement` that has the `gs-icon--coin` class and a `background-image` set in local `PlayerCountry.uss`.

#### 4d. GameMenu (`Assets/UI/Modal/GameMenu/GameMenu.uxml`)

Per the design, the Game Menu shows: **Resume · Save · Exit (destructive)**.  
Current UXML has Resume, Save, Settings, Main Menu — update to match design:
- Remove `btn-settings` and `btn-main-menu`
- Add `btn-exit` with classes `gs-btn gs-btn--primary gs-btn--destructive`

Update `GameMenuDocument.cs` to wire `btn-exit` (exits to main menu / quits).

#### 4e. SelectCountry (`Assets/UI/Modal/SelectCountry/SelectCountry.uxml`)

The design shows:
- **No selection:** hint text (italic) + disabled Start Game + Back  
- **Selected:** country name (`.gs-header`) + Start Game (enabled) + Back

Current state already matches structurally. Verify that:
- `btn-back` uses `.gs-btn--secondary` (already does)
- The hint label is hidden when a country is selected (done in C#)
- The `info-panel` is positioned `bottom:6px; right:6px` in `SelectCountry.uss`

#### 4f. SettingsWindow — layout

The design uses a two-column grid (`auto 1fr`) so toggle groups left-align regardless of label width.  
Update `SettingsWindow.uss` to replace the existing `setting-row` flex layout with a CSS grid equivalent:
```css
.settings-grid {
    /* UI Toolkit has no grid — approximate with flex two-column approach */
}
```
*(UI Toolkit does not support CSS grid — use a horizontal flex row with a fixed-width label column instead.)*

---

### 5. Tooltip locked state

The existing `TooltipController` already adds/removes `tooltip-overlay--pinned` when a tooltip becomes pinned (no C# changes needed).  
Update `.tooltip-overlay--pinned` in `HUD.uss` to use a dashed border matching the design:
```css
.tooltip-overlay--pinned {
    border-style: dashed;
    border-width: 2px;
}
```

---

## Out of scope

- New screens or features
- Game logic changes
- Localization text changes

---

Use /implement to start working on the plan or request changes.
